using CK.Core;
using System.Collections.Generic;

namespace CK.AppIdentity
{
    /// <summary>
    /// Base configuration class for <see cref="ApplicationIdentityPartyConfiguration"/> (root, remotes and tenants)
    /// and <see cref="ApplicationIdentityLocalConfiguration"/>, the <see cref="ILocalParty.LocalConfiguration"/>.
    /// </summary>
    public class ApplicationIdentityConfiguration
    {
        readonly ImmutableConfigurationSection _configuration;
        readonly IReadOnlySet<string> _disallowFeatures;
        readonly IReadOnlySet<string> _allowFeatures;
        readonly AssemblyConfiguration _assemblyConfiguration;

        internal ApplicationIdentityConfiguration( ImmutableConfigurationSection configuration,
                                                   ref InheritedConfigurationProps props )
        {
            _allowFeatures = props.AllowFeatures;
            _disallowFeatures = props.DisallowFeatures;
            _configuration = configuration;
            _assemblyConfiguration = props.AssemblyConfiguration;
        }

        /// <summary>
        /// Gets the configuration section for this object.
        /// </summary>
        public ImmutableConfigurationSection Configuration => _configuration;

        /// <summary>
        /// Gets a set of feature names that are disabled at this level.
        /// No duplicate and no <see cref="AllowFeatures"/> must appear in this set.
        /// </summary>
        public IReadOnlySet<string> DisallowFeatures => _disallowFeatures;

        /// <summary>
        /// Gets a set of feature names that are enabled at this level.
        /// No duplicate and no <see cref="DisallowFeatures"/> must appear in this set.
        /// </summary>
        public IReadOnlySet<string> AllowFeatures => _allowFeatures;

        /// <summary>
        /// Gets the <see cref="AssemblyConfiguration"/> that carries the external allowed assemblies
        /// from which plugins can be looked up for this party.
        /// </summary>
        public AssemblyConfiguration AssemblyConfiguration => _assemblyConfiguration;

        /// <summary>
        /// Computes whether a features is allowed at this level based on <paramref name="isAllowedAbove"/>
        /// and the content of <see cref="AllowFeatures"/> and <see cref="DisallowFeatures"/>.
        /// </summary>
        /// <param name="featureName">The feature name.</param>
        /// <param name="isAllowedAbove">Whether this feature is allowed by default.</param>
        /// <returns>True if the feature is allowed for this level.</returns>
        public bool IsAllowedFeature( string featureName, bool isAllowedAbove )
        {
            if( isAllowedAbove )
            {
                return !DisallowFeatures.Contains( featureName );
            }
            return AllowFeatures.Contains( featureName );
        }
    }
}
