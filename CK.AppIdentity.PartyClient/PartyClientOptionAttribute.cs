using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.AppIdentity;

/// <summary>
/// Decorates an option of a client feature: an <see cref="IPartyComponentOption{TOwner}"/> whose owner is a
/// <see cref="ClientFeature"/>.
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
    /// Gets <see cref="ClientFeature"/>.
    /// </summary>
    public Type OwnerKind => typeof( ClientFeature );
}
