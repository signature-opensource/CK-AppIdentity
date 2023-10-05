using CK.Core;

namespace CK.AppIdentity
{
    /// <summary>
    /// Multiple interface service of <see cref="ApplicationIdentityFeatureDriver"/>
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
        /// Gets whether this feature is allowed by default.
        /// <para>
        /// When a feature is driven by a configuration key (like "GitHubApp"),
        /// this should be initialized to true.
        /// </para>
        /// </summary>
        bool IsRootAllowed { get; }
    }
}
