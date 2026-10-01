using CK.Core;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Creates the folders and files of the store with the access policy of the store.
/// <para>
/// The store is a trust store: a remote's trusted identity is whatever <c>.public</c> file sits in
/// its folder. Anyone able to create, delete or rename entries in a folder repoints that trust, and
/// file permissions don't prevent this: folder permissions do. Hence the two modes:
/// <list type="bullet">
///     <item>
///     A private store (the default <see cref="ApplicationIdentityServiceConfiguration.StoreRootPath"/>)
///     is owner-only. The root is restricted to the current account (Unix 0700, a protected DACL for
///     the current user, SYSTEM and Administrators on Windows), folders are created 0700 and files 0600.
///     </item>
///     <item>
///     A shared store (an explicit <see cref="ApplicationIdentityServiceConfiguration.StoreRootPath"/>)
///     leaves the root permissions to the operator. On Unix, folders and files take the owner and group
///     permissions of the root, never the "others" ones, and the root should be setgid (2770) so that
///     everything below it belongs to the shared group. On Windows, they inherit the root's ACL as-is.
///     </item>
/// </list>
/// In both modes, a root writable by every local account is reported by a warning (that fails
/// the configuration in <see cref="ApplicationIdentityServiceConfiguration.StrictConfigurationMode"/>).
/// </para>
/// <para>
/// On Unix, the mode is applied at creation and then set again: the umask can only remove bits,
/// so the folder or file is never wider than intended, but a group mode can be silently narrowed
/// (0770 becomes 0750 under the usual 022 umask). On Windows, folders and files simply inherit
/// the root's ACL.
/// </para>
/// </summary>
sealed partial class StoreFileSystem
{
    const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    const UnixFileMode GroupDirectory = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup;
    const UnixFileMode GroupFile = UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

    readonly NormalizedPath _root;
    readonly bool _isPrivate;
    readonly object _lock;
    UnixFileMode _directoryMode;
    UnixFileMode _fileMode;
    bool _initialized;
    bool _hasSecurityWarning;

    internal StoreFileSystem( NormalizedPath root, bool isPrivate )
    {
        _root = root;
        _isPrivate = isPrivate;
        _lock = new object();
    }

    /// <summary>
    /// Gets the store root path.
    /// </summary>
    public NormalizedPath Root => _root;

    /// <summary>
    /// Gets whether this store is private (owner-only) or shared.
    /// </summary>
    public bool IsPrivate => _isPrivate;

    /// <summary>
    /// Creates the root if needed, applies the private policy to it and checks it.
    /// This is idempotent and thread safe. IO errors are thrown.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <returns>
    /// False if a security warning has been emitted (the root is writable by every local account
    /// or a private root couldn't be restricted): in StrictConfigurationMode, this must be an error.
    /// </returns>
    public bool Initialize( IActivityLineEmitter logger )
    {
        lock( _lock )
        {
            if( _initialized ) return !_hasSecurityWarning;
            logger.Info( _isPrivate
                            ? $"Using private (owner-only) store '{_root}'."
                            : $"Using shared store '{_root}': its permissions are managed by the operator." );
            if( OperatingSystem.IsWindows() )
            {
                InitializeWindows( logger );
            }
            else
            {
                InitializeUnix( logger );
            }
            _initialized = true;
            return !_hasSecurityWarning;
        }
    }

    void SecurityWarning( IActivityLineEmitter logger, string message, Exception? ex = null )
    {
        _hasSecurityWarning = true;
        logger.Warn( message, ex );
    }

    /// <summary>
    /// Creates a directory (and any missing parent below the root) with this store's policy.
    /// Existing directories are left as-is.
    /// </summary>
    /// <param name="path">The directory path. Must be the root or below it.</param>
    public void CreateDirectory( NormalizedPath path )
    {
        // Not only a debug check: on Unix, the parts beyond the root are appended to it.
        Throw.CheckArgument( path.StartsWith( _root, strict: false ) );
        Initialize( ActivityMonitor.StaticLogger );
        if( OperatingSystem.IsWindows() )
        {
            Directory.CreateDirectory( path );
            return;
        }
        // Directory.CreateDirectory doesn't tell which levels were created: the mode must only
        // be set on the new ones, never on a folder that already existed.
        var current = _root;
        for( int i = _root.Parts.Count; i < path.Parts.Count; ++i )
        {
            current = current.AppendPart( path.Parts[i] );
            if( Directory.Exists( current ) ) continue;
            Directory.CreateDirectory( current, _directoryMode );
            SetCreatedMode( current, _directoryMode );
        }
    }

    /// <summary>
    /// Opens a write stream on a file of the store. Missing parent directories are created
    /// and a created file has this store's mode.
    /// </summary>
    public FileStream OpenWrite( NormalizedPath path, FileMode mode, FileShare share, FileOptions options )
    {
        CreateDirectory( path.RemoveLastPart() );
        var o = new FileStreamOptions { Mode = mode, Access = FileAccess.Write, Share = share, Options = options };
        // UnixCreateMode is not supported on Windows (setting it throws) and is rejected
        // for the modes that never create a file.
        if( OperatingSystem.IsWindows() || mode is FileMode.Open or FileMode.Truncate )
        {
            return new FileStream( path, o );
        }
        bool created = mode == FileMode.CreateNew || !File.Exists( path );
        o.UnixCreateMode = _fileMode;
        var f = new FileStream( path, o );
        if( created )
        {
            try
            {
                File.SetUnixFileMode( f.SafeFileHandle, _fileMode );
            }
            catch( UnauthorizedAccessException )
            {
                // Created by another account meanwhile: it is not ours to change.
            }
            catch
            {
                f.Dispose();
                throw;
            }
        }
        return f;
    }

    /// <summary>
    /// Prefix of the temporary files of <see cref="WriteAtomically"/>: no store file name must start with it.
    /// </summary>
    public const string TempFilePrefix = "$Tmp.";

    /// <summary>
    /// Writes a file atomically: readers see either the previous file or the new one, never a partial one.
    /// The content is written to a temporary file in the same folder, flushed to disk, and then renamed
    /// over the target, but only if <paramref name="write"/> returns normally.
    /// </summary>
    /// <remarks>
    /// The temporary file is locked while it exists so that <see cref="DeleteTemporaryFiles"/> never deletes a write
    /// in progress, even a stalled one: on Windows, it is opened with <see cref="FileShare.None"/> (it can't be deleted)
    /// until the rename. On Unix, it is locked by flock(2) (whatever the .NET FileShare emulation does) and kept open
    /// during the rename (the lock follows the file).
    /// </remarks>
    public void WriteAtomically( NormalizedPath path, Action<Stream> write )
    {
        var temp = GetTempPath( path );
        FileStream? f = null;
        try
        {
            f = OpenTemporaryFile( temp, FileOptions.None );
            write( f );
            f.Flush( flushToDisk: true );
            // On Windows, the file can't be renamed while it is opened without FileShare.Delete.
            if( OperatingSystem.IsWindows() )
            {
                f.Dispose();
                f = null;
            }
            for( int retry = 0; !TryReplace( temp, path, retry ); ++retry )
            {
                Thread.Sleep( GetReplaceDelay( retry ) );
            }
        }
        catch
        {
            f?.Dispose();
            f = null;
            TryDelete( temp );
            throw;
        }
        finally
        {
            f?.Dispose();
        }
    }

    /// <inheritdoc cref="WriteAtomically"/>
    public async Task WriteAtomicallyAsync( NormalizedPath path, Func<Stream, CancellationToken, Task> write, CancellationToken cancel )
    {
        var temp = GetTempPath( path );
        FileStream? f = null;
        try
        {
            f = OpenTemporaryFile( temp, FileOptions.Asynchronous );
            await write( f, cancel ).ConfigureAwait( false );
            await f.FlushAsync( cancel ).ConfigureAwait( false );
            f.Flush( flushToDisk: true );
            if( OperatingSystem.IsWindows() )
            {
                await f.DisposeAsync().ConfigureAwait( false );
                f = null;
            }
            for( int retry = 0; !TryReplace( temp, path, retry ); ++retry )
            {
                await Task.Delay( GetReplaceDelay( retry ), cancel ).ConfigureAwait( false );
            }
        }
        catch
        {
            if( f != null ) await f.DisposeAsync().ConfigureAwait( false );
            f = null;
            TryDelete( temp );
            throw;
        }
        finally
        {
            if( f != null ) await f.DisposeAsync().ConfigureAwait( false );
        }
    }

    FileStream OpenTemporaryFile( NormalizedPath temp, FileOptions options )
    {
        var f = OpenWrite( temp, FileMode.CreateNew, FileShare.None, options );
        if( !OperatingSystem.IsWindows() )
        {
            // Best effort: on a file system that doesn't support locking, only the age of the
            // file protects it from DeleteTemporaryFiles.
            UnixFlock.TryLockExclusive( f.SafeFileHandle, out _ );
        }
        return f;
    }

    /// <summary>
    /// Deletes the temporary files of interrupted atomic writes (older than one hour) in a folder and below it.
    /// A temporary file that is locked (a write is in progress, see <see cref="WriteAtomically"/>) is never deleted.
    /// On Unix, when the file system doesn't support locking, the temporary files are kept.
    /// Errors are ignored.
    /// </summary>
    public static void DeleteTemporaryFiles( NormalizedPath folder )
    {
        try
        {
            var limit = DateTime.UtcNow.AddHours( -1 );
            foreach( var f in Directory.EnumerateFiles( folder, TempFilePrefix + "*", SearchOption.AllDirectories ) )
            {
                if( File.GetLastWriteTimeUtc( f ) >= limit ) continue;
                if( OperatingSystem.IsWindows() )
                {
                    // An opened temporary file can't be deleted (sharing violation).
                    TryDelete( f );
                }
                else
                {
                    TryDeleteUnlockedFile( f );
                }
            }
        }
        catch( Exception )
        {
            // Best effort.
        }
    }

    [UnsupportedOSPlatform( "windows" )]
    static void TryDeleteUnlockedFile( string path )
    {
        try
        {
            // When the .NET FileShare emulation is active, opening a file locked by a writer fails (LOCK_SH | LOCK_NB):
            // this is caught below.
            using var h = File.OpenHandle( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete );
            if( UnixFlock.TryLockExclusive( h, out _ ) == UnixFlock.Result.Locked )
            {
                // Deleted while we hold the lock: no writer can be using it.
                File.Delete( path );
            }
        }
        catch( Exception )
        {
            // Locked, already deleted or not deletable: best effort.
        }
    }

    static NormalizedPath GetTempPath( NormalizedPath path )
    {
        // Same folder: a rename is only atomic within a file system.
        return path.RemoveLastPart().AppendPart( $"{TempFilePrefix}{Guid.NewGuid():N}" );
    }

    static bool TryReplace( NormalizedPath temp, NormalizedPath path, int retry )
    {
        if( OperatingSystem.IsWindows() )
        {
            if( WindowsPosixRename.TryRename( temp, path, out int error ) ) return true;
            if( !WindowsPosixRename.IsNotSupported( error ) )
            {
                // The target is opened without FileShare.Delete (by a reader, an antivirus, an indexer, etc.):
                // this is transient.
                if( retry < 10 ) return false;
                throw new IOException( $"Unable to replace '{path}'.", new Win32Exception( error ) );
            }
        }
        try
        {
            // rename(2) on Unix. On Windows (when the POSIX rename is not supported),
            // MoveFileEx(MOVEFILE_REPLACE_EXISTING) that also fails when the target is open.
            File.Move( temp, path, overwrite: true );
            return true;
        }
        catch( Exception ex ) when( OperatingSystem.IsWindows()
                                    && retry < 10
                                    && (ex is UnauthorizedAccessException || ex.GetType() == typeof( IOException )) )
        {
            return false;
        }
    }

    static TimeSpan GetReplaceDelay( int retry ) => TimeSpan.FromMilliseconds( 10 * (retry + 1) );

    static void TryDelete( string path )
    {
        try
        {
            File.Delete( path );
        }
        catch( Exception )
        {
            // Best effort.
        }
    }

    /// <summary>
    /// Opens a read stream on a file of the store.
    /// </summary>
    public static FileStream OpenRead( NormalizedPath path, FileShare share, FileOptions options )
    {
        return new FileStream( path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = share, Options = options } );
    }

    [UnsupportedOSPlatform( "windows" )]
    static void SetCreatedMode( NormalizedPath path, UnixFileMode mode )
    {
        try
        {
            File.SetUnixFileMode( path, mode );
        }
        catch( UnauthorizedAccessException )
        {
            // Created by another account of the group meanwhile (shared store), with the same policy.
        }
    }

    [UnsupportedOSPlatform( "windows" )]
    void InitializeUnix( IActivityLineEmitter logger )
    {
        if( _isPrivate )
        {
            Directory.CreateDirectory( _root, OwnerDirectory );
            if( File.GetUnixFileMode( _root ) != OwnerDirectory )
            {
                // Fixes a root created before this policy existed: restricting the root is enough
                // to protect everything below it from the other accounts.
                try
                {
                    File.SetUnixFileMode( _root, OwnerDirectory );
                }
                catch( UnauthorizedAccessException ex )
                {
                    SecurityWarning( logger, $"Unable to restrict the private store '{_root}' to its owner.", ex );
                }
            }
            _directoryMode = OwnerDirectory;
            _fileMode = OwnerFile;
        }
        else
        {
            Directory.CreateDirectory( _root );
        }
        var rootMode = File.GetUnixFileMode( _root );
        if( !_isPrivate )
        {
            _directoryMode = OwnerDirectory | (rootMode & GroupDirectory);
            _fileMode = OwnerFile | (rootMode & GroupFile);
            if( (rootMode & UnixFileMode.GroupWrite) != 0 && (rootMode & UnixFileMode.SetGroup) == 0 )
            {
                logger.Info( $"Shared store '{_root}' is group writable but not setgid: what is created below it belongs to the creator's primary group." );
            }
        }
        if( (rootMode & UnixFileMode.OtherWrite) != 0 )
        {
            SecurityWarning( logger, $"Store '{_root}' is writable by every local account: any of them can repoint the trusted identity of a remote." );
        }
    }

    [SupportedOSPlatform( "windows" )]
    void InitializeWindows( IActivityLineEmitter logger )
    {
        var dir = new DirectoryInfo( _root );
        if( _isPrivate )
        {
            var security = CreatePrivateSecurity();
            if( !dir.Exists )
            {
                // FileSystemAclExtensions.Create doesn't create the parents.
                Directory.CreateDirectory( _root.RemoveLastPart() );
                dir.Create( security );
            }
            else if( !dir.GetAccessControl().AreAccessRulesProtected )
            {
                // Fixes a root created before this policy existed. A protected ACL is left as-is:
                // someone deliberately set it (and it is checked below).
                try
                {
                    dir.SetAccessControl( security );
                }
                catch( UnauthorizedAccessException ex )
                {
                    SecurityWarning( logger, $"Unable to restrict the private store '{_root}' to its owner.", ex );
                }
            }
        }
        else
        {
            dir.Create();
        }
        CheckWindowsRootAccess( logger, dir );
    }

    [SupportedOSPlatform( "windows" )]
    static DirectorySecurity CreatePrivateSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection( isProtected: true, preserveInheritance: false );
        var user = WindowsIdentity.GetCurrent().User;
        Throw.CheckState( user != null );
        AddFullControl( security, user );
        AddFullControl( security, new SecurityIdentifier( WellKnownSidType.LocalSystemSid, null ) );
        AddFullControl( security, new SecurityIdentifier( WellKnownSidType.BuiltinAdministratorsSid, null ) );
        return security;

        static void AddFullControl( DirectorySecurity security, SecurityIdentifier sid )
        {
            security.AddAccessRule( new FileSystemAccessRule( sid,
                                                              FileSystemRights.FullControl,
                                                              InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                                                              PropagationFlags.None,
                                                              AccessControlType.Allow ) );
        }
    }

    [SupportedOSPlatform( "windows" )]
    void CheckWindowsRootAccess( IActivityLineEmitter logger, DirectoryInfo dir )
    {
        const FileSystemRights writeRights = FileSystemRights.WriteData
                                             | FileSystemRights.AppendData
                                             | FileSystemRights.Delete
                                             | FileSystemRights.DeleteSubdirectoriesAndFiles
                                             | FileSystemRights.ChangePermissions
                                             | FileSystemRights.TakeOwnership;
        var rules = dir.GetAccessControl().GetAccessRules( includeExplicit: true, includeInherited: true, typeof( SecurityIdentifier ) );
        foreach( FileSystemAccessRule r in rules )
        {
            // Inherit-only rules (like CREATOR OWNER) don't apply to the root itself.
            if( r.AccessControlType != AccessControlType.Allow
                || (r.PropagationFlags & PropagationFlags.InheritOnly) != 0
                || (r.FileSystemRights & writeRights) == 0
                || r.IdentityReference is not SecurityIdentifier sid
                || !(sid.IsWellKnown( WellKnownSidType.WorldSid )
                     || sid.IsWellKnown( WellKnownSidType.BuiltinUsersSid )
                     || sid.IsWellKnown( WellKnownSidType.AuthenticatedUserSid )
                     || sid.IsWellKnown( WellKnownSidType.InteractiveSid )) )
            {
                continue;
            }
            SecurityWarning( logger, $"Store '{_root}' is writable by '{GetDisplayName( sid )}': any local account can repoint the trusted identity of a remote." );
        }

        static string GetDisplayName( SecurityIdentifier sid )
        {
            try
            {
                return sid.Translate( typeof( NTAccount ) ).Value;
            }
            catch( IdentityNotMappedException )
            {
                return sid.Value;
            }
        }
    }

}
