using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// States a fact about the party clients built from the decorated type: the engine that builds a kind of client (the
/// TypeScript engine for the client applications, for instance) unions the facts of the types a client is built from,
/// once per build, into the <see cref="ClientApplicationDescription.Facts"/> of the client. The client announces them
/// with its party's description, so that the other parties know what it is.
/// <para>
/// A fact is an atomic trait of the <see cref="ClientApplicationDescription.FactContext"/>: a name without the '|'
/// separator and without whitespace ("Enlistment"). The attribute can be repeated, and it is not inherited.
/// </para>
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public sealed class PartyClientFactAttribute : Attribute
{
    /// <summary>
    /// Initializes a new fact.
    /// </summary>
    /// <param name="fact">The fact: a name without the '|' separator and without whitespace.</param>
    public PartyClientFactAttribute( string fact )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( fact );
        Throw.CheckArgument( "A fact is a name without the '|' separator and without whitespace.",
                             fact.IndexOf( ClientApplicationDescription.FactContext.Separator ) < 0 && !fact.AsSpan().ContainsAny( " \t\r\n" ) );
        Fact = fact;
    }

    /// <summary>
    /// Gets the fact.
    /// </summary>
    public string Fact { get; }

    /// <summary>
    /// Gets the fact as an atomic trait of the <see cref="ClientApplicationDescription.FactContext"/>.
    /// </summary>
    public CKTrait Trait => ClientApplicationDescription.FactContext.FindOrCreate( Fact );
}
