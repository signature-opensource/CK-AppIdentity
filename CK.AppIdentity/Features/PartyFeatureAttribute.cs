using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// The side of an asymmetric feature (see <see cref="PartyFeatureAttribute"/>).
/// </summary>
public enum FeatureSide
{
    /// <summary>
    /// A symmetric feature.
    /// </summary>
    None,

    /// <summary>
    /// The Local side: a local party that declares the feature <b>is</b> the feature.
    /// </summary>
    Local,

    /// <summary>
    /// The Remote side: a local party relays the feature to the remote that is the feature.
    /// </summary>
    Remote
}

/// <summary>
/// Declares the feature of a feature driver (a concrete <see cref="ApplicationIdentityFeatureDriver"/>): its name and its
/// activation. It is not inherited: each driver declares its own feature.
/// <list type="bullet">
///   <item>
///   <c>[PartyFeature( "HttpEndpoint", TypeActivation.Optional )]</c>: a party has the feature only when it declares it;
///   <see cref="TypeActivation.ByDefault"/>: a party has it unless it turns it off; <see cref="TypeActivation.Required"/>:
///   a party always has it.
///   </item>
///   <item>
///   <c>[PartyFeature( "AuthCenter", TypeActivation.Optional, Side = FeatureSide.Local )]</c> and
///   <c>[PartyFeature( "AuthCenter", TypeActivation.Optional, Side = FeatureSide.Remote )]</c>: the two drivers of an
///   asymmetric feature. The Local side is declared by "AuthCenter", the Remote side by "AuthCenterRemote" (their
///   configuration names, see <see cref="IConfigurableTypeAttribute.Name"/>); they agree on their activation.
///   </item>
/// </list>
/// The engine reads it (the feature names, the activation, the sides): the drivers get them from the
/// <see cref="PartyComponentDirectory"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class PartyFeatureAttribute : Attribute, IConfigurableTypeAttribute
{
    /// <summary>
    /// Initializes a new attribute.
    /// </summary>
    /// <param name="featureName">The feature name.</param>
    /// <param name="activation">The activation of the feature.</param>
    public PartyFeatureAttribute( string featureName, TypeActivation activation )
    {
        FeatureName = featureName;
        Activation = activation;
    }

    /// <summary>
    /// Gets the feature name. The configuration name of the Remote side of an asymmetric feature is this name followed
    /// by "Remote" (see <see cref="IConfigurableTypeAttribute.Name"/>).
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Gets or sets the side of an asymmetric feature.
    /// </summary>
    public FeatureSide Side { get; init; }

    /// <inheritdoc />
    public TypeActivation Activation { get; }

    string IConfigurableTypeAttribute.Name => Side == FeatureSide.Remote ? FeatureName + "Remote" : FeatureName;
}
