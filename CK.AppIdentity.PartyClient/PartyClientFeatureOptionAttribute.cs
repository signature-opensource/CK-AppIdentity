using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.AppIdentity;

/// <summary>
/// Decorates an option of a client feature: an <see cref="IPartyComponentOption{TOwner}"/> whose owner is a
/// <see cref="ClientFeature"/>. Its name is its key in the section of the client feature.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class PartyClientFeatureOptionAttribute : Attribute, IPartyComponentOptionAttribute
{
    /// <summary>
    /// Initializes a new <see cref="PartyClientFeatureOptionAttribute"/>.
    /// </summary>
    /// <param name="name">The configuration name of the option: its key in the section of its client feature.</param>
    /// <param name="activation">The activation of the option.</param>
    /// <param name="callerFilePath">Automatically set by the compiler.</param>
    public PartyClientFeatureOptionAttribute( string name, TypeActivation activation, [CallerFilePath] string? callerFilePath = null )
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
    /// Gets <see cref="ClientFeature"/>.
    /// </summary>
    public Type OwnerKind => typeof( ClientFeature );
}
