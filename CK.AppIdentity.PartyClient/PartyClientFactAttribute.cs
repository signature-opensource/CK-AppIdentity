using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// States a fact about the party clients built from the decorated type: the engine that builds a kind of client (the
/// TypeScript engine for the client applications, for instance) unions the facts of the types a client is built from,
/// once per build, into the <see cref="ClientApplicationDescription.Facts"/> of the client. The client announces them
/// with its party's description, so that the other parties know what it is.
/// <para>
/// A fact is a key with an optional value (see <see cref="ClientApplicationFacts"/>): <c>[PartyClientFact( "Admin" )]</c>,
/// <c>[PartyClientFact( "Enlistment", "app-identity/enlist" )]</c>. The attribute can be repeated, and it is not inherited.
/// </para>
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public sealed class PartyClientFactAttribute : Attribute
{
    /// <summary>
    /// Initializes a new fact.
    /// </summary>
    /// <param name="key">The key: a non empty name without whitespace, '=' nor ','.</param>
    /// <param name="value">The value: empty for a fact without value.</param>
    public PartyClientFactAttribute( string key, string value = "" )
    {
        Throw.CheckArgument( "A fact key is a non empty name without whitespace, '=' nor ','.", ClientApplicationFacts.IsValidKey( key ) );
        Throw.CheckArgument( "A fact value has no control character.", ClientApplicationFacts.IsValidValue( value ) );
        Key = key;
        Value = value;
    }

    /// <summary>
    /// Gets the key of the fact.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the value of the fact: empty for a fact without value.
    /// </summary>
    public string Value { get; }
}
