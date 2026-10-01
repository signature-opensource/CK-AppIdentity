using CK.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Offers basic file system functionalities to the parties shared or private folders.
/// <para>
/// The paths given to this store are full paths that must follow these rules, otherwise an
/// <see cref="ArgumentException"/> is thrown:
/// <list type="bullet">
///     <item>They must be in <see cref="FolderPath"/> (the comparison is case sensitive). Only <see cref="CreateDirectory"/>
///     accepts the <see cref="FolderPath"/> itself.</item>
///     <item>Their parts below <see cref="FolderPath"/> must not be '.' or '..' and must not end with a '.' or a space
///     (Windows silently removes them).</item>
///     <item>They must not be in the <see cref="TrashBinPath"/> or in the "-Local" folder (the <see cref="ILocalParty.LocalFileStore"/>
///     of a local party): these names are compared case insensitively, and the first part below <see cref="FolderPath"/>
///     must not contain a '~' (Windows 8.3 short names like "$TRASH~1").</item>
///     <item>File names must not start with "$Tmp." (temporary files of <see cref="WriteAtomically"/>) or "$Lock."
///     (lock files of <see cref="TryAcquireLock"/>), case insensitively.</item>
/// </list>
/// These rules are the same on all platforms.
/// </para>
/// </summary>
public interface IFileStore
{
    /// <summary>
    /// Gets the <see cref="IParty"/>' folder that this store handles.
    /// </summary>
    NormalizedPath FolderPath { get; }

    /// <summary>
    /// Gets the trash bin path.
    /// <para>
    /// Trashed files are currently kept forever: the trash bin housekeeping is not implemented yet.
    /// </para>
    /// </summary>
    NormalizedPath TrashBinPath { get; }

    /// <summary>
    /// Tries to move a file that must be in <see cref="FolderPath"/> to the <see cref="TrashBinPath"/>.
    /// This returns true (no error) if the file doesn't exist.
    /// <para>
    /// The <paramref name="fullPath"/> must follow the path rules of the store (see <see cref="IFileStore"/>),
    /// otherwise an <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="logger">A <see cref="IActivityMonitor"/>, a <see cref="IParallelLogger"/> and even the <see cref="ActivityMonitor.StaticLogger"/> can be used.</param>
    /// <param name="fullPath">The full path of the file to trash.</param>
    /// <param name="immediateDelete">Optionally tries to delete the file immediately instead of moving it to the bin.</param>
    /// <returns>True on success, false on error.</returns>
    bool TryTrash( IActivityLineEmitter logger, in NormalizedPath fullPath, bool immediateDelete = false );

    /// <summary>
    /// Creates a directory, and any missing parent directory, with the store's access policy
    /// (see <see cref="ApplicationIdentityServiceConfiguration.StoreRootPath"/>).
    /// Directories that already exist are left as-is.
    /// <para>
    /// The <paramref name="fullPath"/> must be <see cref="FolderPath"/> or follow the path rules of the store
    /// (see <see cref="IFileStore"/>), otherwise an <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="fullPath">The full path of the directory to create.</param>
    void CreateDirectory( in NormalizedPath fullPath );

    /// <summary>
    /// Opens a write stream on a file in <see cref="FolderPath"/>. Missing parent directories are created
    /// and a created file has the store's access policy (see <see cref="ApplicationIdentityServiceConfiguration.StoreRootPath"/>).
    /// <para>
    /// The policy only applies when the file is created: a file that already exists (opened with
    /// <see cref="FileMode.Open"/>, <see cref="FileMode.Truncate"/>, <see cref="FileMode.Append"/>, etc.)
    /// keeps its current permissions.
    /// </para>
    /// <para>
    /// The <paramref name="fullPath"/> must follow the path rules of the store (see <see cref="IFileStore"/>),
    /// otherwise an <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="fullPath">The full path of the file to write.</param>
    /// <param name="mode">How the file is opened or created.</param>
    /// <param name="share">How the file can be shared with other streams while it is open.</param>
    /// <param name="options">Advanced options.</param>
    /// <returns>The write stream.</returns>
    Stream OpenWriteStream( in NormalizedPath fullPath,
                            FileMode mode = FileMode.Create,
                            FileShare share = FileShare.None,
                            FileOptions options = FileOptions.None );

    /// <summary>
    /// Writes a file atomically: readers see either the previous file or the new one, never a partial one, even if the
    /// process crashes. The content is written to a temporary file in the same folder (with the store's access policy),
    /// flushed to disk, and then renamed over the target, but only if <paramref name="write"/> returns normally: when it
    /// throws, the target is left untouched. Missing parent directories are created.
    /// <para>
    /// The replaced file's permissions are not kept: the new file always has the store's access policy. In a shared store,
    /// this only requires the write permission on the folder, not on the replaced file (that may belong to another account).
    /// </para>
    /// <para>
    /// The <paramref name="write"/> callback must not dispose the stream. On Windows, the target can't be replaced while it is
    /// opened without <see cref="FileShare.Delete"/>: the rename is retried during about half a second before giving up.
    /// </para>
    /// <para>
    /// The <paramref name="fullPath"/> must follow the path rules of the store (see <see cref="IFileStore"/>),
    /// otherwise an <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="fullPath">The full path of the file to write.</param>
    /// <param name="write">Writes the content.</param>
    void WriteAtomically( in NormalizedPath fullPath, Action<Stream> write );

    /// <inheritdoc cref="WriteAtomically"/>
    /// <param name="fullPath">The full path of the file to write.</param>
    /// <param name="write">Writes the content.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    Task WriteAtomicallyAsync( NormalizedPath fullPath, Func<Stream, CancellationToken, Task> write, CancellationToken cancel = default );

    /// <summary>
    /// Tries to acquire a named inter-process lock in <see cref="FolderPath"/> without waiting.
    /// See <see cref="FileLock"/>.
    /// </summary>
    /// <param name="name">The lock name. Must be a valid file name that doesn't end with a '.' or a space.</param>
    /// <returns>The lock or null if it is held by someone else.</returns>
    FileLock? TryAcquireLock( string name );

    /// <summary>
    /// Tries to acquire a named inter-process lock in <see cref="FolderPath"/>, waiting at most <paramref name="timeout"/>.
    /// See <see cref="FileLock"/>.
    /// </summary>
    /// <param name="name">The lock name. Must be a valid file name that doesn't end with a '.' or a space.</param>
    /// <param name="timeout">The maximal time to wait. Can be <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The lock or null if it is still held by someone else after <paramref name="timeout"/>.</returns>
    Task<FileLock?> TryAcquireLockAsync( string name, TimeSpan timeout, CancellationToken cancel = default );

    /// <summary>
    /// Opens a read stream on an existing file in <see cref="FolderPath"/>.
    /// <para>
    /// The default <paramref name="share"/> includes <see cref="FileShare.Delete"/> so that, on Windows, an open reader
    /// doesn't prevent the file from being replaced by <see cref="WriteAtomically"/>.
    /// </para>
    /// <para>
    /// The <paramref name="fullPath"/> must follow the path rules of the store (see <see cref="IFileStore"/>),
    /// otherwise an <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="fullPath">The full path of the file to read.</param>
    /// <param name="share">How the file can be shared with other streams while it is open.</param>
    /// <param name="options">Advanced options.</param>
    /// <returns>The read stream.</returns>
    Stream OpenReadStream( in NormalizedPath fullPath,
                           FileShare share = FileShare.Read | FileShare.Delete,
                           FileOptions options = FileOptions.None );
}
