using CK.Core;
using System;

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
}
