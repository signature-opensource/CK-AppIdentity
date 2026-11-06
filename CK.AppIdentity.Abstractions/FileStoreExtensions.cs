using CK.Core;
using System;
using System.IO;

namespace CK.AppIdentity;

/// <summary>
/// Extends <see cref="IFileStore"/>.
/// </summary>
public static class FileStoreExtensions
{
    /// <summary>
    /// Creates or replaces a file atomically with the store's access policy. See <see cref="IFileStore.WriteAtomically"/>.
    /// </summary>
    /// <param name="store">This store.</param>
    /// <param name="fullPath">The full path of the file to write.</param>
    /// <param name="content">The bytes to write.</param>
    public static void WriteAllBytes( this IFileStore store, in NormalizedPath fullPath, ReadOnlyMemory<byte> content )
    {
        store.WriteAtomically( fullPath, f => f.Write( content.Span ) );
    }

    /// <summary>
    /// Reads the content of an existing file. The file is opened by <see cref="IFileStore.OpenReadStream"/>: this
    /// read never prevents another party (or process) from replacing or trashing the file.
    /// <para>
    /// A file that doesn't exist throws a <see cref="FileNotFoundException"/> (or a <see cref="DirectoryNotFoundException"/>).
    /// Use <see cref="TryReadAllBytes"/> when the file may disappear meanwhile.
    /// </para>
    /// </summary>
    /// <param name="store">This store.</param>
    /// <param name="fullPath">The full path of the file to read.</param>
    /// <returns>The file content.</returns>
    public static byte[] ReadAllBytes( this IFileStore store, in NormalizedPath fullPath )
    {
        using var s = store.OpenReadStream( fullPath, options: FileOptions.SequentialScan );
        return StoreFileReader.ReadToEnd( s );
    }

    /// <summary>
    /// Reads the content of a file, or returns null if it doesn't exist. See <see cref="ReadAllBytes"/>.
    /// <para>
    /// A file listed in a folder can be gone when it is read: another party (or process) sharing the store may have
    /// replaced or trashed it meanwhile. A null result means "gone", never "invalid": any other error is thrown.
    /// </para>
    /// </summary>
    /// <param name="store">This store.</param>
    /// <param name="fullPath">The full path of the file to read.</param>
    /// <returns>The file content or null if the file (or its folder) doesn't exist.</returns>
    public static byte[]? TryReadAllBytes( this IFileStore store, in NormalizedPath fullPath )
    {
        try
        {
            return ReadAllBytes( store, fullPath );
        }
        catch( Exception ex ) when( ex is FileNotFoundException or DirectoryNotFoundException )
        {
            return null;
        }
    }
}
