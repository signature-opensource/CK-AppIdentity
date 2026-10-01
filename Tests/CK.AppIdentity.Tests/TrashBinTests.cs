using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class TrashBinTests
{
    static readonly TimeSpan _timeout = TimeSpan.FromSeconds( 5 );

    static ApplicationIdentityService CreateService( string partyName, string? trashBinRetention = null )
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = $"Test/${partyName}";
            if( trashBinRetention != null ) c["TrashBinRetention"] = trashBinRetention;
        } );
        Throw.DebugAssert( config != null );
        var services = new ServiceCollection();
        services.AddSingleton( config );
        services.AddSingleton<ApplicationIdentityService>();
        return services.BuildServiceProvider().GetRequiredService<ApplicationIdentityService>();
    }

    static void EmptyTrashBin( IFileStore store )
    {
        if( Directory.Exists( store.TrashBinPath ) ) Directory.Delete( store.TrashBinPath, recursive: true );
    }

    [Test]
    public void TrashBinRetention_configuration()
    {
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Test/$App" )
            .ShouldNotBeNull().TrashBinRetention.ShouldBe( TimeSpan.FromDays( 7 ) );
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Test/$App";
            c["TrashBinRetention"] = "12:00:00";
        } ).ShouldNotBeNull().TrashBinRetention.ShouldBe( TimeSpan.FromHours( 12 ) );
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Test/$App";
            c["TrashBinRetention"] = "-01:00:00";
        } ).ShouldBeNull();
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Test/$App";
            c["TrashBinRetention"] = "a week";
        } ).ShouldBeNull();
        ApplicationIdentityServiceConfiguration.CreateEmpty().TrashBinRetention.ShouldBe( TimeSpan.FromDays( 7 ) );
    }

    [Test]
    public async Task trashed_files_are_named_with_their_trash_time_Async()
    {
        await using var s = CreateService( "TrashName" );
        var store = s.SharedFileStore;
        EmptyTrashBin( store );
        var file = store.FolderPath.AppendPart( "Data.txt" );
        store.WriteAllBytes( file, new byte[] { 1 } );
        // The trash time is the trash time, not the file's last write time.
        File.SetLastWriteTimeUtc( file, new DateTime( 2020, 1, 1, 0, 0, 0, DateTimeKind.Utc ) );

        var before = DateTime.UtcNow.AddSeconds( -1 );
        store.TryTrash( TestHelper.Monitor, file ).ShouldBeTrue();

        var trashed = Directory.GetFiles( store.TrashBinPath ).Single( f => !f.EndsWith( ".binInfo" ) );
        var name = Path.GetFileName( trashed );
        name.Length.ShouldBe( 16 + 1 + 32, "yyyyMMddTHHmmssZ-guid: no extension, it is in the .binInfo." );
        DateTime.ParseExact( name.Substring( 0, 16 ), "yyyyMMdd'T'HHmmss'Z'", null,
                             System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal )
            .ShouldBeGreaterThanOrEqualTo( before.AddTicks( -before.Ticks % TimeSpan.TicksPerSecond ) );
        File.ReadAllText( trashed + ".binInfo" ).ShouldBe( "Data.txt" );
    }

    [Test]
    public async Task old_trashed_files_are_purged_when_the_service_starts_Async()
    {
        await using var s = CreateService( "TrashStart" );
        var bin = s.SharedFileStore.TrashBinPath;
        EmptyTrashBin( s.SharedFileStore );
        Directory.CreateDirectory( bin );
        string Create( string name, DateTime? binInfoTime = null )
        {
            var path = Path.Combine( bin, name );
            File.WriteAllText( path, "x" );
            File.WriteAllText( path + ".binInfo", "Original.txt" );
            if( binInfoTime.HasValue ) File.SetLastWriteTimeUtc( path + ".binInfo", binInfoTime.Value );
            return path;
        }
        var now = DateTime.UtcNow;
        var old = Create( $"20200101T000000Z-{Guid.NewGuid():N}.txt" );
        var recent = Create( $"{now:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid():N}.txt" );
        // Before the trash time was in the name: the .binInfo time is the trash time.
        var legacyOld = Create( $"{Guid.NewGuid()}.txt", binInfoTime: now.AddDays( -8 ) );
        var legacyRecent = Create( $"{Guid.NewGuid()}.txt", binInfoTime: now.AddDays( -6 ) );
        var orphanInfo = Path.Combine( bin, $"{Guid.NewGuid()}.txt.binInfo" );
        File.WriteAllText( orphanInfo, "Lost.txt" );

        await s.StartAndInitializeAsync().WaitAsync( _timeout );

        File.Exists( old ).ShouldBeFalse();
        File.Exists( old + ".binInfo" ).ShouldBeFalse();
        File.Exists( legacyOld ).ShouldBeFalse();
        File.Exists( orphanInfo ).ShouldBeFalse();
        File.Exists( recent ).ShouldBeTrue();
        File.Exists( recent + ".binInfo" ).ShouldBeTrue();
        File.Exists( legacyRecent ).ShouldBeTrue();
    }

    [TestCase( "Data.binInfo" )]
    [TestCase( "Data.BININFO" )]
    public async Task a_trashed_file_can_have_the_binInfo_extension_Async( string fileName )
    {
        var s = CreateService( "TrashBinInfo" );
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        var store = s.SharedFileStore;
        EmptyTrashBin( store );
        var file = store.FolderPath.AppendPart( fileName );
        store.WriteAllBytes( file, new byte[] { 1 } );
        store.TryTrash( TestHelper.Monitor, file ).ShouldBeTrue();
        // Shutdown purges with the 7 days retention: the trashed file is kept.
        await s.DisposeAsync();

        var entries = Directory.GetFiles( store.TrashBinPath ).Select( Path.GetFileName ).ToArray();
        entries.Length.ShouldBe( 2 );
        var trashed = entries.Single( n => !n!.EndsWith( ".binInfo" ) )!;
        File.ReadAllText( Path.Combine( store.TrashBinPath, trashed + ".binInfo" ) ).ShouldBe( fileName );

        // A zero retention deletes it with its .binInfo: nothing is left.
        var s0 = CreateService( "TrashBinInfo", trashBinRetention: "00:00:00" );
        await s0.StartAndInitializeAsync().WaitAsync( _timeout );
        await s0.DisposeAsync();
        Directory.GetFiles( store.TrashBinPath ).ShouldBeEmpty();
    }

    [Test]
    public async Task a_zero_retention_purges_the_trash_bins_at_shutdown_Async()
    {
        var s = CreateService( "TrashZero", trashBinRetention: "00:00:00" );
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        foreach( var store in new[] { s.SharedFileStore, s.LocalFileStore } )
        {
            var file = store.FolderPath.AppendPart( "Data.txt" );
            store.WriteAllBytes( file, new byte[] { 1 } );
            store.TryTrash( TestHelper.Monitor, file ).ShouldBeTrue();
            Directory.GetFiles( store.TrashBinPath ).ShouldNotBeEmpty();
        }
        await s.DisposeAsync();
        Directory.GetFiles( s.SharedFileStore.TrashBinPath ).ShouldBeEmpty();
        Directory.GetFiles( s.LocalFileStore.TrashBinPath ).ShouldBeEmpty();
    }

    [Test]
    public async Task destroying_a_party_applies_the_retention_to_its_trash_bin_Async()
    {
        var s = CreateService( "TrashDestroy", trashBinRetention: "00:00:00" );
        await using var _s = s;
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        var r = await s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "TrashedRemote" ).WaitAsync( _timeout );
        Throw.DebugAssert( r != null );
        var file = r.SharedFileStore.FolderPath.AppendPart( "Data.txt" );
        r.SharedFileStore.WriteAllBytes( file, new byte[] { 1 } );
        r.SharedFileStore.TryTrash( TestHelper.Monitor, file ).ShouldBeTrue();

        await r.DestroyAsync().WaitAsync( _timeout );

        Directory.GetFiles( r.SharedFileStore.TrashBinPath ).ShouldBeEmpty();
        Directory.Exists( r.SharedFileStore.FolderPath ).ShouldBeTrue( "The folder is shared by the applications: it is kept." );
    }
}
