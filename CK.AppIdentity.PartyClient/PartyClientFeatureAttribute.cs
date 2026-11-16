using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// Declares a client feature (a concrete <see cref="ClientFeature"/>): its name is its key in the
/// "Local:PartyClient:{Name}" section of its client (<c>[PartyClientFeature( "NgApp", TypeActivation.Optional )]</c>).
/// The engine reads it (see <see cref="PartyComponentInfo.FeatureName"/>). It is not inherited: each client feature
/// declares its own name.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class PartyClientFeatureAttribute : Attribute, IConfigurableTypeAttribute
{
    /// <summary>
    /// Initializes a new <see cref="PartyClientFeatureAttribute"/>.
    /// </summary>
    /// <param name="name">The configuration name of the client feature.</param>
    /// <param name="activation">The activation of the client feature.</param>
    public PartyClientFeatureAttribute( string name, TypeActivation activation )
    {
        Name = name;
        Activation = activation;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TypeActivation Activation { get; }
}
