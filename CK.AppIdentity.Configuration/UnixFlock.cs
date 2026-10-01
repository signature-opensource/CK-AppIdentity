using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CK.AppIdentity;

/// <summary>
/// Direct flock(2) on an opened file. This doesn't depend on the .NET FileShare emulation, that
/// DOTNET_SYSTEM_IO_DISABLEFILELOCKING (or the System.IO.DisableFileLocking AppContext switch) disables.
/// When the emulation is active, the file opened with <see cref="System.IO.FileShare.None"/> is already
/// LOCK_EX on its descriptor: calling this is a no-op.
/// <para>
/// Used by <see cref="CK.Core.FileLock"/> and by the store's atomic writes.
/// </para>
/// </summary>
[UnsupportedOSPlatform( "windows" )]
static class UnixFlock
{
    const int LOCK_EX = 2;
    const int LOCK_NB = 4;
    const int EINTR = 4;
    static readonly int EWOULDBLOCK = OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ? 11 : 35;

    /// <summary>
    /// Gets the errno value of EWOULDBLOCK: this is the HResult of the IOException thrown by the .NET
    /// FileShare emulation when the file is locked.
    /// </summary>
    public static int WouldBlock => EWOULDBLOCK;

    /// <summary>
    /// The result of <see cref="TryLockExclusive"/>.
    /// </summary>
    public enum Result
    {
        /// <summary>
        /// The lock is held on the descriptor.
        /// </summary>
        Locked,

        /// <summary>
        /// Another descriptor holds the lock.
        /// </summary>
        Busy,

        /// <summary>
        /// The file system doesn't support file locking (some network or FUSE mounts).
        /// </summary>
        NotSupported
    }

    /// <summary>
    /// Tries to take a LOCK_EX on the file without waiting.
    /// </summary>
    /// <param name="handle">The opened file.</param>
    /// <param name="errno">The errno when <see cref="Result.NotSupported"/> is returned.</param>
    /// <returns>The result.</returns>
    public static Result TryLockExclusive( SafeFileHandle handle, out int errno )
    {
        bool added = false;
        handle.DangerousAddRef( ref added );
        try
        {
            int fd = (int)handle.DangerousGetHandle();
            for(; ; )
            {
                if( flock( fd, LOCK_EX | LOCK_NB ) == 0 )
                {
                    errno = 0;
                    return Result.Locked;
                }
                errno = Marshal.GetLastPInvokeError();
                if( errno == EINTR ) continue;
                return errno == EWOULDBLOCK ? Result.Busy : Result.NotSupported;
            }
        }
        finally
        {
            if( added ) handle.DangerousRelease();
        }
    }

    [DllImport( "libc", SetLastError = true )]
    static extern int flock( int fd, int operation );
}
