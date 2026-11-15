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
/// Declares the feature of a feature driver (an <see cref="ApplicationIdentityFeatureDriver"/>): its name and whether it is
/// optional. It decorates <see cref="ApplicationIdentityFeatureDriver"/>: a driver without its own attribute manages the
/// feature named by its type name without the "FeatureDriver" suffix, and is optional (a party has it only when it
/// declares it). A driver declares its own attribute to change this:
/// <list type="bullet">
///   <item><c>[PartyFeature( IsOptional = false )]</c>: the feature is allowed by default (a party has it unless it turns it off).</item>
///   <item>
///   <c>[PartyFeature( "AuthCenter", Side = FeatureSide.Local )]</c> and <c>[PartyFeature( "AuthCenter", Side = FeatureSide.Remote )]</c>:
///   the two drivers of an asymmetric feature. The Local side is declared by "AuthCenter", the Remote side by
///   "AuthCenterRemote" (their feature names, see <see cref="INamedFeatureAttribute"/>).
///   </item>
/// </list>
/// The engine reads it (the feature names, the optionality, the sides): the drivers get them from the
/// <see cref="PartyComponentDirectory"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class PartyFeatureAttribute : Attribute, INamedFeatureAttribute, IOptionalResourceGroupAttribute
{
    /// <summary>
    /// The suffix of the type name of a feature driver.
    /// </summary>
    public const string DriverTypeNameSuffix = "FeatureDriver";

    /// <summary>
    /// Initializes a new attribute: the feature name is the type name without its "FeatureDriver" suffix.
    /// </summary>
    public PartyFeatureAttribute()
    {
    }

    /// <summary>
    /// Initializes a new attribute with an explicit feature name (required by the drivers of an asymmetric feature).
    /// </summary>
    /// <param name="featureName">The feature name.</param>
    public PartyFeatureAttribute( string featureName )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( featureName );
        FeatureName = featureName;
    }

    /// <summary>
    /// Gets the explicit feature name. Null when it is the type name without the "FeatureDriver" suffix.
    /// </summary>
    public string? FeatureName { get; }

    /// <summary>
    /// Gets or sets the side of an asymmetric feature. When it is not <see cref="FeatureSide.None"/>, the <see cref="FeatureName"/>
    /// is required.
    /// </summary>
    public FeatureSide Side { get; init; }

    /// <summary>
    /// Gets or sets whether the feature is optional: a party has it only when it declares it. Defaults to true. False
    /// makes the feature allowed by default (a party has it unless it turns it off).
    /// </summary>
    public bool IsOptional { get; init; } = true;

    string? INamedFeatureAttribute.Name => FeatureName == null ? null : Side == FeatureSide.Remote ? FeatureName + "Remote" : FeatureName;

    string? INamedFeatureAttribute.TypeNameSuffix => FeatureName == null ? DriverTypeNameSuffix : null;
}
