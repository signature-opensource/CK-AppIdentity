using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.AppIdentity;

/// <summary>
/// Decorates an option of a party client: an <see cref="IPartyComponentOption{TOwner}"/> whose owner is a
/// <see cref="PartyClient"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class PartyClientOptionAttribute : Attribute, IPartyComponentOptionAttribute
{
    /// <summary>
    /// Initializes a new <see cref="PartyClientOptionAttribute"/>.
    /// </summary>
    /// <param name="callerFilePath">Automatically set by the compiler.</param>
    public PartyClientOptionAttribute( [CallerFilePath] string? callerFilePath = null )
    {
        CallerFilePath = callerFilePath;
    }

    /// <inheritdoc />
    public string? CallerFilePath { get; }

    /// <inheritdoc />
    public bool IsEnabledByDefault { get; init; }

    /// <summary>
    /// Gets <see cref="PartyClient"/>.
    /// </summary>
    public Type OwnerKind => typeof( PartyClient );
}
