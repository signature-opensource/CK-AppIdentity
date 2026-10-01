using CK.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

sealed class FileStore : IFileStore
{
    /// <summary>
    /// The name of the trash bin folder.
    /// </summary>
    internal const string TrashBinName = "$TrashBin";

    /// <summary>
    /// The name of the local party's <see cref="ILocalParty.LocalFileStore"/> folder in its shared store.
    /// </summary>
    internal const string LocalStoreName = "-Local";

    const string LockFilePrefix = "$Lock.";

    readonly StoreFileSystem _fileSystem;
    NormalizedPath _folderPath;
    NormalizedPath _binPath;

    internal FileStore( StoreFileSystem fileSystem, in NormalizedPath folderPath )
    {
        _fileSystem = fileSystem;
        _folderPath = folderPath;
        _binPath = folderPath.AppendPart( TrashBinName );
        fileSystem.CreateDirectory( _folderPath );
        StoreFileSystem.DeleteTemporaryFiles( _folderPath );
    }

    public NormalizedPath FolderPath => _folderPath;

    public NormalizedPath TrashBinPath => _binPath;

    public bool TryTrash( IActivityLineEmitter logger, in NormalizedPath fullPath, bool immediateDelete = false )
    {
        CheckPath( fullPath, allowFolderPath: false );
        if( !File.Exists( fullPath ) ) return true;
        if( immediateDelete ) return TryDelete( logger, fullPath );
        var targetPath = $"{_binPath.Path}/{Guid.NewGuid()}{Path.GetExtension( fullPath.Path )}";
        try
        {
            _fileSystem.CreateDirectory( _binPath );
            File.Move( fullPath, targetPath );
            using var info = new StreamWriter( _fileSystem.OpenWrite( targetPath + ".binInfo", FileMode.Create, FileShare.None, FileOptions.None ) );
            info.Write( fullPath.RemovePrefix( _folderPath ) );
            return true;
        }
        catch( Exception ex )
        {
            logger.Error( $"While moving '{fullPath}' to the trash bin.", ex );
            return false;
        }
    }

    public void CreateDirectory( in NormalizedPath fullPath )
    {
        CheckPath( fullPath, allowFolderPath: true );
        _fileSystem.CreateDirectory( fullPath );
    }

    public Stream OpenWriteStream( in NormalizedPath fullPath,
                                   FileMode mode = FileMode.Create,
                                   FileShare share = FileShare.None,
                                   FileOptions options = FileOptions.None )
    {
        CheckPath( fullPath, allowFolderPath: false );
        return _fileSystem.OpenWrite( fullPath, mode, share, options );
    }

    public Stream OpenReadStream( in NormalizedPath fullPath,
                                  FileShare share = FileShare.Read | FileShare.Delete,
                                  FileOptions options = FileOptions.None )
    {
        CheckPath( fullPath, allowFolderPath: false );
        return StoreFileSystem.OpenRead( fullPath, share, options );
    }

    public void WriteAtomically( in NormalizedPath fullPath, Action<Stream> write )
    {
        Throw.CheckNotNullArgument( write );
        CheckPath( fullPath, allowFolderPath: false );
        _fileSystem.WriteAtomically( fullPath, write );
    }

    public Task WriteAtomicallyAsync( NormalizedPath fullPath, Func<Stream, CancellationToken, Task> write, CancellationToken cancel = default )
    {
        Throw.CheckNotNullArgument( write );
        CheckPath( fullPath, allowFolderPath: false );
        return _fileSystem.WriteAtomicallyAsync( fullPath, write, cancel );
    }

    public FileLock? TryAcquireLock( string name )
    {
        var path = EnsureLockFile( name );
        return FileLock.TryAcquire( path );
    }

    public Task<FileLock?> TryAcquireLockAsync( string name, TimeSpan timeout, CancellationToken cancel = default )
    {
        var path = EnsureLockFile( name );
        return FileLock.TryAcquireAsync( path, timeout, cancel );
    }

    NormalizedPath EnsureLockFile( string name )
    {
        Throw.CheckNotNullOrEmptyArgument( name );
        Throw.CheckArgument( "Lock name must be a valid file name that doesn't end with a '.' or a space.",
                             name.IndexOfAny( Path.GetInvalidFileNameChars() ) < 0
                             && name.IndexOfAny( ['/', '\\'] ) < 0 // On Unix, '\' is valid but NormalizedPath splits on it.
                             && !name.EndsWith( '.' ) && !name.EndsWith( ' ' ) );
        var path = _folderPath.AppendPart( LockFilePrefix + name );
        // The lock file is created once (and never deleted) through the store, with its access policy:
        // in a shared store, the other accounts of the group must be able to open it to take the lock.
        // FileLock would create it owner-only.
        if( !File.Exists( path ) )
        {
            try
            {
                _fileSystem.OpenWrite( path, FileMode.CreateNew, FileShare.ReadWrite, FileOptions.None ).Dispose();
            }
            catch( IOException ) when( File.Exists( path ) )
            {
                // Created meanwhile. Any other error is thrown: FileLock would otherwise create the
                // file owner-only and the other accounts of a shared store could never take the lock.
            }
        }
        return path;
    }

    /// <summary>
    /// The path rules (see <see cref="IFileStore"/>).
    /// </summary>
    void CheckPath( in NormalizedPath fullPath, bool allowFolderPath )
    {
        // The containment is ordinal (case sensitive): this rejects paths that may be in this folder on
        // a case insensitive file system, never the reverse.
        Throw.CheckArgument( "Path must be in the FolderPath of the store.", fullPath.StartsWith( _folderPath, strict: !allowFolderPath ) );
        int first = _folderPath.Parts.Count;
        for( int i = first; i < fullPath.Parts.Count; ++i )
        {
            var part = fullPath.Parts[i];
            // Dotted parts would escape the containment check. Windows silently removes trailing
            // dots and spaces: "$TrashBin." is "$TrashBin".
            Throw.CheckArgument( "Path parts must not be '.' or '..' and must not end with a '.' or a space.",
                                 part != "." && part != ".." && !part.EndsWith( '.' ) && !part.EndsWith( ' ' ) );
        }
        if( fullPath.Parts.Count > first )
        {
            // The excluded folders are compared case insensitively, and the '~' prevents Windows 8.3 aliases
            // (like "$TRASH~1"): this is the same on all platforms so that the rules don't depend on the file system.
            var top = fullPath.Parts[first];
            Throw.CheckArgument( "The trash bin, the local store ('-Local') and '~' names can't be accessed below the FolderPath.",
                                 !top.Equals( TrashBinName, StringComparison.OrdinalIgnoreCase )
                                 && !top.Equals( LocalStoreName, StringComparison.OrdinalIgnoreCase )
                                 && !top.Contains( '~' ) );
        }
        if( !allowFolderPath )
        {
            var name = fullPath.LastPart;
            Throw.CheckArgument( "File names starting with '$Tmp.' or '$Lock.' are reserved.",
                                 !name.StartsWith( StoreFileSystem.TempFilePrefix, StringComparison.OrdinalIgnoreCase )
                                 && !name.StartsWith( LockFilePrefix, StringComparison.OrdinalIgnoreCase ) );
        }
    }

    bool TryDelete( IActivityLineEmitter logger, NormalizedPath fullPath )
    {
        try
        {
            File.Delete( fullPath );
            return true;
        }
        catch( Exception ex )
        {
            logger.Error( $"While deleting file '{fullPath}'.", ex );
            return false;
        }
    }

    internal void OnShutdownOrDestroyed( IActivityMonitor monitor, bool isDestroyed )
    {
        // TODO: $TrashBin housekeeping.
    }

}
