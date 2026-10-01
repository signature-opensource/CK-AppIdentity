using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

/// <summary>
/// The store holds trust anchors: anyone able to create, delete or rename files in it repoints the
/// trusted identity of a remote. A default store is owner-only, an explicit one is shared and its
/// permissions are the operator's, but never open to every local account.
/// </summary>
[TestFixture]
public class FileStoreTests
{
    const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Test]
    public void default_store_is_private_and_explicit_store_is_shared()
    {
        var root = CreateSharedRoot();
        try
        {
            var defaultConfig = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Test/$Private" );
            Throw.DebugAssert( defaultConfig != null );
            defaultConfig.IsPrivateStore.ShouldBeTrue();
            defaultConfig.StoreRootPath.ShouldBe( ApplicationIdentityServiceConfiguration.DefaultStoreRootPath );

            var explicitConfig = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "Test/$Shared";
                c["StoreRootPath"] = root;
            } );
            Throw.DebugAssert( explicitConfig != null );
            explicitConfig.IsPrivateStore.ShouldBeFalse();
            explicitConfig.StoreRootPath.ShouldBe( root );

            ApplicationIdentityServiceConfiguration.CreateEmpty().IsPrivateStore.ShouldBeTrue();
            ApplicationIdentityServiceConfiguration.CreateEmpty( storeRootPath: root ).IsPrivateStore.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    [Test]
    public void private_store_root_is_restricted_to_its_owner()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Test/$Private" );
        Throw.DebugAssert( config != null );
        if( OperatingSystem.IsWindows() )
        {
            CheckOwnerOnlyAccess( config.StoreRootPath );
        }
        else
        {
            File.GetUnixFileMode( config.StoreRootPath ).ShouldBe( OwnerDirectory );
        }
    }

    [SupportedOSPlatform( "windows" )]
    static void CheckOwnerOnlyAccess( string path )
    {
        var security = new DirectoryInfo( path ).GetAccessControl();
        security.AreAccessRulesProtected.ShouldBeTrue( "No permission is inherited from the parent folders." );
        var sids = security.GetAccessRules( true, true, typeof( SecurityIdentifier ) )
                           .Cast<FileSystemAccessRule>()
                           .Select( r => (SecurityIdentifier)r.IdentityReference )
                           .ToHashSet();
        sids.ShouldBeSubsetOf( [WindowsIdentity.GetCurrent().User!,
                                new SecurityIdentifier( WellKnownSidType.LocalSystemSid, null ),
                                new SecurityIdentifier( WellKnownSidType.BuiltinAdministratorsSid, null )] );
    }

    [Test]
    public async Task private_store_writes_reads_and_creates_parent_folders_Async()
    {
        await using var s = CreateService( storeRootPath: null );
        var store = s.SharedFileStore;
        var file = store.FolderPath.Combine( $"{Guid.NewGuid()}/Sub/Data.bin" );

        store.WriteAllBytes( file, new byte[] { 1, 2, 3 } );
        using( var r = store.OpenReadStream( file ) )
        {
            var content = new byte[4];
            r.ReadAtLeast( content, 3, throwOnEndOfStream: false ).ShouldBe( 3 );
            content[..3].ShouldBe( [1, 2, 3] );
        }
        using( var w = store.OpenWriteStream( file, FileMode.Append ) )
        {
            w.WriteByte( 4 );
        }
        File.ReadAllBytes( file ).ShouldBe( [1, 2, 3, 4] );

        if( !OperatingSystem.IsWindows() )
        {
            File.GetUnixFileMode( file ).ShouldBe( OwnerFile );
            File.GetUnixFileMode( file.RemoveLastPart() ).ShouldBe( OwnerDirectory );
            File.GetUnixFileMode( file.RemoveLastPart( 2 ) ).ShouldBe( OwnerDirectory );
        }
        Directory.Delete( file.RemoveLastPart( 2 ), recursive: true );
    }

    [Test]
    public async Task paths_outside_the_store_are_rejected_Async()
    {
        await using var s = CreateService( storeRootPath: null );
        var store = s.SharedFileStore;

        Should.Throw<ArgumentException>( () => store.OpenWriteStream( store.FolderPath.Combine( "../Escaped.txt" ) ) );
        Should.Throw<ArgumentException>( () => store.OpenWriteStream( store.FolderPath.RemoveLastPart().AppendPart( "Other.txt" ) ) );
        Should.Throw<ArgumentException>( () => store.OpenWriteStream( store.TrashBinPath.AppendPart( "Bin.txt" ) ) );
        Should.Throw<ArgumentException>( () => store.OpenWriteStream( store.FolderPath ) );
        Should.Throw<ArgumentException>( () => store.OpenReadStream( store.FolderPath.Combine( "./File.txt" ) ) );
        Should.Throw<ArgumentException>( () => store.CreateDirectory( store.TrashBinPath ) );
        Should.Throw<ArgumentException>( () => store.CreateDirectory( store.FolderPath.RemoveLastPart() ) );

        Should.NotThrow( () => store.CreateDirectory( store.FolderPath ) );
    }

    [TestCase( "$trashbin/x.txt", Description = "Case insensitive file systems." )]
    [TestCase( "$TrashBin./x.txt", Description = "Windows removes trailing dots." )]
    [TestCase( "$TrashBin /x.txt", Description = "Windows removes trailing spaces." )]
    [TestCase( "$TRASH~1/x.txt", Description = "Windows 8.3 short name." )]
    [TestCase( "-Local/x.txt", Description = "The local store is private to the party." )]
    [TestCase( "-local/$TrashBin/x.txt", Description = "Even its trash bin." )]
    [TestCase( "x./y.txt", Description = "Trailing dots are rejected everywhere." )]
    [TestCase( "x/../../Other/y.txt", Description = "Dotted parts." )]
    public async Task aliases_of_excluded_folders_are_rejected_by_all_operations_Async( string relative )
    {
        await using var s = CreateService( storeRootPath: null );
        var store = s.SharedFileStore;
        var path = store.FolderPath.Combine( relative );

        Should.Throw<ArgumentException>( () => store.OpenWriteStream( path ) );
        Should.Throw<ArgumentException>( () => store.OpenReadStream( path ) );
        Should.Throw<ArgumentException>( () => store.WriteAllBytes( path, new byte[] { 1 } ) );
        Should.Throw<ArgumentException>( () => store.CreateDirectory( path ) );
        Should.Throw<ArgumentException>( () => store.TryTrash( TestHelper.Monitor, path, immediateDelete: true ) );
    }

    [Test]
    public async Task reserved_files_cannot_be_trashed_Async()
    {
        await using var s = CreateService( storeRootPath: null );
        var store = s.SharedFileStore;
        using( var l = store.TryAcquireLock( "Trash" ) )
        {
            l.ShouldNotBeNull();
            Should.Throw<ArgumentException>( () => store.TryTrash( TestHelper.Monitor, l.Path, immediateDelete: true ) );
        }
        Should.Throw<ArgumentException>( () => store.TryTrash( TestHelper.Monitor, store.FolderPath.AppendPart( "$tmp.x" ) ) );
    }

    [Test]
    public async Task the_local_store_is_still_usable_through_its_own_store_Async()
    {
        await using var s = CreateService( storeRootPath: null );
        var file = s.LocalFileStore.FolderPath.AppendPart( "Private.bin" );
        s.LocalFileStore.WriteAllBytes( file, new byte[] { 1 } );
        File.ReadAllBytes( file ).ShouldBe( new byte[] { 1 } );
        Should.Throw<ArgumentException>( () => s.SharedFileStore.OpenReadStream( file ) );
        File.Delete( file );
    }

    [Test]
    public void CreateEmpty_validates_and_checks_the_store_root()
    {
        Should.Throw<ArgumentException>( () => ApplicationIdentityServiceConfiguration.CreateEmpty( storeRootPath: "Relative/Store" ) );

        var root = CreateSharedRoot();
        try
        {
            ApplicationIdentityServiceConfiguration.CreateEmpty( environmentName: "#Prod", storeRootPath: root ).StrictConfigurationMode.ShouldBeTrue();
            if( OperatingSystem.IsWindows() )
            {
                GrantEveryoneWrite( root );
            }
            else
            {
                File.SetUnixFileMode( root, OwnerDirectory | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute );
            }
            Should.Throw<InvalidOperationException>( () => ApplicationIdentityServiceConfiguration.CreateEmpty( environmentName: "#Prod", storeRootPath: root ) );
            ApplicationIdentityServiceConfiguration.CreateEmpty( storeRootPath: root ).StrictConfigurationMode.ShouldBeFalse( "#Dev: only a warning." );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    [Test]
    public async Task trashed_files_keep_their_original_path_Async()
    {
        await using var s = CreateService( storeRootPath: null );
        var store = s.SharedFileStore;
        var name = $"{Guid.NewGuid()}.txt";
        var file = store.FolderPath.AppendPart( name );
        store.WriteAllBytes( file, new byte[] { 42 } );

        store.TryTrash( TestHelper.Monitor, file ).ShouldBeTrue();

        File.Exists( file ).ShouldBeFalse();
        Directory.EnumerateFiles( store.TrashBinPath, "*.binInfo" )
                 .Select( File.ReadAllText )
                 .ShouldContain( name );
    }

    [Test]
    public async Task shared_store_items_take_the_root_permissions_Async()
    {
        var root = CreateSharedRoot();
        try
        {
            if( OperatingSystem.IsWindows() )
            {
                var before = new DirectoryInfo( root ).GetAccessControl().GetSecurityDescriptorSddlForm( AccessControlSections.Access );
                await using var s = CreateService( root );
                new DirectoryInfo( root ).GetAccessControl().GetSecurityDescriptorSddlForm( AccessControlSections.Access )
                    .ShouldBe( before, "The root permissions of a shared store are the operator's." );
            }
            else
            {
                var groupRoot = OwnerDirectory | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup;
                File.SetUnixFileMode( root, groupRoot );
                await using var s = CreateService( root );
                var file = s.SharedFileStore.FolderPath.AppendPart( "Shared.bin" );
                s.SharedFileStore.WriteAllBytes( file, new byte[] { 1 } );

                File.GetUnixFileMode( root ).ShouldBe( groupRoot );
                File.GetUnixFileMode( s.SharedFileStore.FolderPath ).ShouldBe( groupRoot, "Even under the usual 022 umask." );
                File.GetUnixFileMode( file ).ShouldBe( OwnerFile | UnixFileMode.GroupRead | UnixFileMode.GroupWrite );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    [Test]
    public void a_store_writable_by_every_local_account_fails_in_strict_mode()
    {
        var root = CreateSharedRoot();
        try
        {
            CreateStrict( root ).ShouldNotBeNull( "The freshly created root is clean." );

            if( OperatingSystem.IsWindows() )
            {
                GrantEveryoneWrite( root );
            }
            else
            {
                File.SetUnixFileMode( root, OwnerDirectory | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute );
            }
            CreateStrict( root ).ShouldBeNull();
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }

        static ApplicationIdentityServiceConfiguration? CreateStrict( string root )
        {
            return ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "Test/$Shared";
                c["StoreRootPath"] = root;
                c["StrictConfigurationMode"] = "true";
            } );
        }
    }

    [SupportedOSPlatform( "windows" )]
    static void GrantEveryoneWrite( string root )
    {
        var dir = new DirectoryInfo( root );
        var security = dir.GetAccessControl();
        security.AddAccessRule( new FileSystemAccessRule( new SecurityIdentifier( WellKnownSidType.WorldSid, null ),
                                                          FileSystemRights.Write,
                                                          InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                                                          PropagationFlags.None,
                                                          AccessControlType.Allow ) );
        dir.SetAccessControl( security );
    }

    // Shared roots are created inside the (private) test store so that their initial permissions
    // don't depend on the machine: they inherit the owner-only ones.
    static NormalizedPath CreateSharedRoot()
    {
        // Initializes (restricts) the private test store.
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Test/$Private" ).ShouldNotBeNull();
        var root = ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.Combine( $"SharedRoots/{Guid.NewGuid()}" );
        Directory.CreateDirectory( root );
        return root;
    }

    static ApplicationIdentityService CreateService( NormalizedPath? storeRootPath )
    {
        var config = ApplicationIdentityServiceConfiguration.CreateEmpty( "Test", "FileStore", storeRootPath: storeRootPath );
        return new ApplicationIdentityService( config, new ServiceCollection().BuildServiceProvider() );
    }
}
