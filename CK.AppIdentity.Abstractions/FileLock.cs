using CK.AppIdentity;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Core;

/// <summary>
/// Inter-process exclusive lock held on a lock file. The lock is released by <see cref="Dispose"/> and,
/// since it is held by the operating system, when the process dies: a lock can't be left behind by a
/// crashed holder.
/// <para>
/// On Windows, the lock is the file opened with <see cref="FileShare.None"/> (enforced by the kernel).
/// On Unix, the lock is a <c>flock(LOCK_EX)</c> taken directly: it doesn't depend on the .NET
/// <see cref="FileShare"/> emulation, so it holds even when <c>DOTNET_SYSTEM_IO_DISABLEFILELOCKING</c>
/// (or the <c>System.IO.DisableFileLocking</c> AppContext switch) is set. On a file system that doesn't
/// support <c>flock</c> (some network or FUSE mounts), acquiring the lock throws an <see cref="IOException"/>:
/// it never silently proceeds without holding the lock.
/// </para>
/// <para>
/// Usage notes:
/// <list type="bullet">
///     <item>Locks work between processes on the same machine. File locking over network shares is not reliable
///     enough to be relied upon across machines.</item>
///     <item>A lock is not reentrant: acquiring a lock already held by this process (even by the same thread) fails.
///     It is not bound to a thread: it can be released from any thread (it can be held across awaits).</item>
///     <item>The lock file is never deleted: on Unix, deleting and recreating it would let two processes each hold
///     a lock, each on a different file.</item>
///     <item>Any account that can open the lock file (read access is enough) can take the lock, and hold it forever.
///     The lock file should be in a folder restricted to the accounts that cooperate.</item>
///     <item>When the lock file doesn't exist, it is created owner-only on Unix (and inherits the folder ACL on Windows):
///     only this account can then take the lock. To share a lock between accounts, create the lock file beforehand
///     with the appropriate permissions: the lock only opens it.</item>
/// </list>
/// </para>
/// </summary>
public sealed class FileLock : IDisposable
{
    const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    static readonly TimeSpan _maxDelay = TimeSpan.FromMilliseconds( 500 );

    readonly FileStream _file;
    readonly NormalizedPath _path;

    FileLock( FileStream file, NormalizedPath path )
    {
        _file = file;
        _path = path;
    }

    /// <summary>
    /// Gets the lock file path.
    /// </summary>
    public NormalizedPath Path => _path;

    /// <summary>
    /// Tries to acquire the lock without waiting.
    /// The lock file is created (owner-only) if it doesn't exist, its folder must exist.
    /// </summary>
    /// <param name="path">The lock file path.</param>
    /// <returns>The lock or null if it is held by someone else.</returns>
    public static FileLock? TryAcquire( NormalizedPath path )
    {
        Throw.CheckArgument( !path.IsEmptyPath );
        FileStream file;
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.Read, Share = FileShare.None };
            if( !OperatingSystem.IsWindows() ) options.UnixCreateMode = OwnerOnly;
            file = new FileStream( path, options );
        }
        catch( IOException ex ) when( IsHeldBySomeoneElse( ex ) )
        {
            return null;
        }
        if( OperatingSystem.IsWindows() ) return new FileLock( file, path );
        try
        {
            if( UnixLock( file.SafeFileHandle, path ) ) return new FileLock( file, path );
        }
        catch
        {
            file.Dispose();
            throw;
        }
        file.Dispose();
        return null;
    }

    /// <summary>
    /// Tries to acquire the lock, waiting at most <paramref name="timeout"/>.
    /// The lock file is created (owner-only) if it doesn't exist, its folder must exist.
    /// <para>
    /// There is no operating system notification for file locks: waiting polls with a backoff (up to 500 ms).
    /// </para>
    /// </summary>
    /// <param name="path">The lock file path.</param>
    /// <param name="timeout">The maximal time to wait. Can be <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The lock or null if it is still held by someone else after <paramref name="timeout"/>.</returns>
    public static async Task<FileLock?> TryAcquireAsync( NormalizedPath path,
                                                        TimeSpan timeout,
                                                        CancellationToken cancel = default )
    {
        Throw.CheckArgument( timeout >= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan );
        var start = Stopwatch.GetTimestamp();
        var delay = TimeSpan.FromMilliseconds( 10 );
        for(; ; )
        {
            cancel.ThrowIfCancellationRequested();
            var l = TryAcquire( path );
            if( l != null ) return l;
            var wait = delay;
            if( timeout != Timeout.InfiniteTimeSpan )
            {
                var remaining = timeout - Stopwatch.GetElapsedTime( start );
                if( remaining <= TimeSpan.Zero ) return null;
                if( remaining < wait ) wait = remaining;
            }
            await Task.Delay( wait, cancel ).ConfigureAwait( false );
            delay *= 2;
            if( delay > _maxDelay ) delay = _maxDelay;
        }
    }

    /// <summary>
    /// Releases the lock.
    /// </summary>
    public void Dispose() => _file.Dispose();

    static bool IsHeldBySomeoneElse( IOException ex )
    {
        if( OperatingSystem.IsWindows() )
        {
            // ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION.
            int code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33;
        }
        // When the .NET FileShare emulation is active, it takes a flock(LOCK_EX | LOCK_NB) on open and
        // reports EWOULDBLOCK as an IOException whose HResult is the raw errno. Other plain IOExceptions
        // (EMFILE, EIO, ENOSPC, EROFS, etc.) are real errors that must not be mistaken for "busy".
        return ex.GetType() == typeof( IOException ) && ex.HResult == UnixFlock.WouldBlock;
    }

    [UnsupportedOSPlatform( "windows" )]
    static bool UnixLock( SafeFileHandle handle, NormalizedPath path )
    {
        // When the .NET emulation is active, it already holds LOCK_EX on this very descriptor and
        // this is a no-op. When it is disabled, this is the lock.
        switch( UnixFlock.TryLockExclusive( handle, out int errno ) )
        {
            case UnixFlock.Result.Locked: return true;
            case UnixFlock.Result.Busy: return false;
            default:
                throw new IOException( $"Unable to lock '{path}' (errno {errno}): its file system doesn't support file locking. The lock file must be on a local file system." );
        }
    }
}
