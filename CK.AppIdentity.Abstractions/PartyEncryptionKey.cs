using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// The encryption key that a party publishes in its description (see <see cref="PartyDescription.EncryptionKey"/>): its
/// public key and the signature of this public key by the party's identity key, whose expiration is the end of use of
/// the key. Anyone who knows the party's identity verifies it, whatever the path it came by.
/// <para>
/// This is only bytes: the format of the public key, the verification of the signature and the encryption belong to the
/// key management (CK.AppIdentity.KeyManagement).
/// </para>
/// </summary>
public sealed class PartyEncryptionKey : IEquatable<PartyEncryptionKey>
{
    readonly byte[] _publicKey;
    readonly byte[] _signature;

    /// <summary>
    /// Initializes a new published encryption key. The bytes are copied.
    /// </summary>
    /// <param name="publicKey">The public key. Must not be empty.</param>
    /// <param name="signature">The encoded signature of the public key by the party. Must not be empty.</param>
    public PartyEncryptionKey( ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> signature )
    {
        Throw.CheckArgument( publicKey.Length > 0 );
        Throw.CheckArgument( signature.Length > 0 );
        _publicKey = publicKey.ToArray();
        _signature = signature.ToArray();
    }

    /// <summary>
    /// Gets the public key.
    /// </summary>
    public ReadOnlyMemory<byte> PublicKey => _publicKey;

    /// <summary>
    /// Gets the encoded signature of the <see cref="PublicKey"/> by the party.
    /// </summary>
    public ReadOnlyMemory<byte> Signature => _signature;

    /// <inheritdoc />
    public bool Equals( PartyEncryptionKey? other ) => other != null
                                                       && _publicKey.AsSpan().SequenceEqual( other._publicKey )
                                                       && _signature.AsSpan().SequenceEqual( other._signature );

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as PartyEncryptionKey );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( _publicKey.Length, _signature.Length, _publicKey.Length > 0 ? _publicKey[^1] : 0 );

    /// <summary>
    /// Returns the beginning of the hexadecimal SHA-256 of the public key (a readable identifier).
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( _publicKey ), 0, 6 );
}
