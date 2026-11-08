using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.AppIdentity;

/// <summary>
/// Decorates an option of a feature driver: an <see cref="IPartyComponentOption{TOwner}"/> whose owner is an
/// <see cref="IApplicationIdentityFeatureDriver"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class FeatureOptionAttribute : Attribute, IPartyComponentOptionAttribute
{
    /// <summary>
    /// Initializes a new <see cref="FeatureOptionAttribute"/>.
    /// </summary>
    /// <param name="callerFilePath">Automatically set by the compiler.</param>
    public FeatureOptionAttribute( [CallerFilePath] string? callerFilePath = null )
    {
        CallerFilePath = callerFilePath;
    }

    /// <inheritdoc />
    public string? CallerFilePath { get; }

    /// <inheritdoc />
    public bool IsEnabledByDefault { get; init; }

    /// <summary>
    /// Gets <see cref="IApplicationIdentityFeatureDriver"/>.
    /// </summary>
    public Type OwnerKind => typeof( IApplicationIdentityFeatureDriver );
}
