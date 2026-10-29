using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Multiple interface service of <c>"ApplicationIdentityFeatureDriver"</c>
/// that is the only base class to use to implement a feature driver.
/// </summary>
[IsMultiple]
public interface IApplicationIdentityFeatureDriver : ISingletonAutoService
{
    /// <summary>
    /// Gets this feature name.
    /// This name is derived from the implementation type name: "XXXFeatureDriver"
    /// drives the feature "XXX".
    /// </summary>
    string FeatureName { get; }

    /// <summary>
    /// Gets whether this feature is allowed for the root application: its "Local" configuration (that resolves the
    /// "Defaults" of the root) or, when it doesn't say, whether the feature is allowed by default.
    /// </summary>
    bool IsRootAllowed { get; }
}
