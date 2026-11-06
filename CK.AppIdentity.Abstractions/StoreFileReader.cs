using System;
using System.IO;

namespace CK.AppIdentity;

/// <summary>
/// Reads the files of a store from outside of it (tests, tools) the way the store does (see <see cref="IFileStore.OpenReadStream"/>):
/// the file is shared for reading and deleting, so that this read never prevents the parties that use the store from
/// replacing or trashing it (on Windows, a reader that doesn't share the deletion does).
/// <para>
/// Code that has an <see cref="IFileStore"/> uses <see cref="FileStoreExtensions.ReadAllBytes"/> and
/// <see cref="FileStoreExtensions.TryReadAllBytes"/>: they also check the path rules of the store.
/// </para>
/// </summary>
public static class StoreFileReader
{
    /// <summary>
    /// Reads the content of an existing file.
    /// </summary>
    /// <param name="fullPath">The full path of the file to read.</param>
    /// <returns>The file content.</returns>
    public static byte[] ReadAllBytes( string fullPath )
    {
        using var s = new FileStream( fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.SequentialScan );
        return ReadToEnd( s );
    }

    /// <summary>
    /// Reads the content of a file, or returns null if it doesn't exist (it may have been replaced or trashed meanwhile).
    /// Any other error is thrown.
    /// </summary>
    /// <param name="fullPath">The full path of the file to read.</param>
    /// <returns>The file content or null if the file (or its folder) doesn't exist.</returns>
    public static byte[]? TryReadAllBytes( string fullPath )
    {
        try
        {
            return ReadAllBytes( fullPath );
        }
        catch( Exception ex ) when( ex is FileNotFoundException or DirectoryNotFoundException )
        {
            return null;
        }
    }

    internal static byte[] ReadToEnd( Stream s )
    {
        // The length of an open file can't change under a reader that shares only the reading and the deletion: a
        // writer can't open it, a replacement is a new file.
        var bytes = new byte[s.Length];
        s.ReadExactly( bytes );
        return bytes;
    }
}
