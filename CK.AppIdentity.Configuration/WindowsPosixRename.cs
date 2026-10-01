using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CK.AppIdentity;

/// <summary>
/// Renames a file over an existing one with the POSIX semantics (Windows 10 1809+, NTFS): like rename(2),
/// this is atomic and succeeds even when the replaced file is open (with <see cref="System.IO.FileShare.Delete"/>),
/// whereas MoveFileEx (used by File.Move) fails with an access denied.
/// File.Replace (ReplaceFile) also handles open files but is not atomic: one of its failure modes leaves
/// the target missing.
/// </summary>
[SupportedOSPlatform( "windows" )]
static class WindowsPosixRename
{
    const uint DELETE = 0x00010000;
    const uint SYNCHRONIZE = 0x00100000;
    const uint FILE_SHARE_ALL = 7;
    const uint OPEN_EXISTING = 3;
    const int FileRenameInfoEx = 22;
    const uint FILE_RENAME_FLAG_REPLACE_IF_EXISTS = 0x1;
    const uint FILE_RENAME_FLAG_POSIX_SEMANTICS = 0x2;

    /// <summary>
    /// Tries to rename <paramref name="source"/> over <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The full path of the file to rename.</param>
    /// <param name="target">The full path of the file to replace (it may not exist).</param>
    /// <param name="error">The Win32 error on failure.</param>
    /// <returns>True on success.</returns>
    public static bool TryRename( string source, string target, out int error )
    {
        using var h = CreateFileW( source, DELETE | SYNCHRONIZE, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero );
        if( h.IsInvalid )
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }
        // FILE_RENAME_INFO: ULONG Flags, HANDLE RootDirectory (pointer aligned), ULONG FileNameLength, WCHAR FileName[].
        var name = Encoding.Unicode.GetBytes( target );
        int rootOffset = IntPtr.Size;
        int lengthOffset = rootOffset + IntPtr.Size;
        int nameOffset = lengthOffset + sizeof( uint );
        var info = new byte[nameOffset + name.Length + sizeof( char )];
        BitConverter.TryWriteBytes( info.AsSpan( 0 ), FILE_RENAME_FLAG_REPLACE_IF_EXISTS | FILE_RENAME_FLAG_POSIX_SEMANTICS );
        BitConverter.TryWriteBytes( info.AsSpan( lengthOffset ), (uint)name.Length );
        name.CopyTo( info, nameOffset );
        if( SetFileInformationByHandle( h, FileRenameInfoEx, info, (uint)info.Length ) )
        {
            error = 0;
            return true;
        }
        error = Marshal.GetLastPInvokeError();
        return false;
    }

    /// <summary>
    /// Gets whether the error means that the file system (or the Windows version) doesn't support
    /// the POSIX rename: MoveFileEx should be used instead.
    /// </summary>
    /// <param name="error">The Win32 error.</param>
    /// <returns>True if the POSIX rename is not supported.</returns>
    public static bool IsNotSupported( int error )
    {
        // ERROR_INVALID_FUNCTION, ERROR_NOT_SUPPORTED, ERROR_INVALID_PARAMETER.
        return error is 1 or 50 or 87;
    }

    [DllImport( "kernel32", SetLastError = true, CharSet = CharSet.Unicode )]
    static extern SafeFileHandle CreateFileW( string fileName, uint access, uint share, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template );

    [DllImport( "kernel32", SetLastError = true )]
    [return: MarshalAs( UnmanagedType.Bool )]
    static extern bool SetFileInformationByHandle( SafeFileHandle file, int infoClass, byte[] info, uint size );
}
