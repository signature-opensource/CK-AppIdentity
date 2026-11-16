using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.AppIdentity;

/// <summary>
/// Decorates an option of a feature driver: an <see cref="IPartyComponentOption{TOwner}"/> whose owner is an
/// <see cref="IApplicationIdentityFeatureDriver"/>. Its name is its key in the section of the feature
/// (<c>[PartyFeatureOption( "PasswordLost", TypeActivation.ByDefault )]</c> is <c>"AuthCenter": { "PasswordLost": ... }</c>).
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class PartyFeatureOptionAttribute : Attribute, IPartyComponentOptionAttribute
{
    /// <summary>
    /// Initializes a new <see cref="PartyFeatureOptionAttribute"/>.
    /// </summary>
    /// <param name="name">The configuration name of the option: its key in the section of its feature.</param>
    /// <param name="activation">The activation of the option.</param>
    /// <param name="callerFilePath">Automatically set by the compiler.</param>
    public PartyFeatureOptionAttribute( string name, TypeActivation activation, [CallerFilePath] string? callerFilePath = null )
    {
        Name = name;
        Activation = activation;
        CallerFilePath = callerFilePath;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TypeActivation Activation { get; }

    /// <inheritdoc />
    public string? CallerFilePath { get; }

    /// <summary>
    /// Gets <see cref="IApplicationIdentityFeatureDriver"/>.
    /// </summary>
    public Type OwnerKind => typeof( IApplicationIdentityFeatureDriver );
}
