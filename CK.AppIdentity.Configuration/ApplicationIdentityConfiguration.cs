using CK.Core;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// Base configuration class for <see cref="ApplicationIdentityPartyConfiguration"/> (root, remotes and tenants)
/// and <see cref="ApplicationIdentityLocalConfiguration"/>, the <see cref="ILocalParty.LocalConfiguration"/>.
/// <para>
/// The features and the assemblies are the ones of a party: a remote or a local party (the "Local" section of the root
/// or of a tenant domain). They are resolved from the "Defaults" sections above the party (from the root down) and the
/// party's own keys, the nearest level winning. The root and the tenant domains are structural levels: their own
/// <see cref="AllowFeatures"/>, <see cref="DisallowFeatures"/> and <see cref="AssemblyConfiguration"/> are empty, the
/// ones of their local party are in their <see cref="ILocalParty.LocalConfiguration"/>.
/// </para>
/// </summary>
public class ApplicationIdentityConfiguration
{
    readonly ImmutableConfigurationSection _configuration;
    readonly IReadOnlySet<string> _disallowFeatures;
    readonly IReadOnlySet<string> _allowFeatures;
    readonly AssemblyConfiguration _assemblyConfiguration;

    internal ApplicationIdentityConfiguration( ImmutableConfigurationSection configuration,
                                               ref ResolvedConfigurationProps props )
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
    /// Gets the feature names that are disabled for this party.
    /// No <see cref="AllowFeatures"/> appear in this set.
    /// </summary>
    public IReadOnlySet<string> DisallowFeatures => _disallowFeatures;

    /// <summary>
    /// Gets the feature names that are enabled for this party.
    /// No <see cref="DisallowFeatures"/> appear in this set.
    /// </summary>
    public IReadOnlySet<string> AllowFeatures => _allowFeatures;

    /// <summary>
    /// Gets the <see cref="AssemblyConfiguration"/> that carries the external allowed assemblies
    /// from which plugins can be looked up for this party.
    /// </summary>
    public AssemblyConfiguration AssemblyConfiguration => _assemblyConfiguration;

    /// <summary>
    /// Computes whether a feature is allowed for this party: it is in <see cref="AllowFeatures"/>, or it is
    /// not in <see cref="DisallowFeatures"/> and <paramref name="isAllowedByDefault"/> is true.
    /// </summary>
    /// <param name="featureName">The feature name.</param>
    /// <param name="isAllowedByDefault">Whether this feature is allowed by default (an opt-out feature).</param>
    /// <returns>True if the feature is allowed for this party.</returns>
    public bool IsAllowedFeature( string featureName, bool isAllowedByDefault )
    {
        if( isAllowedByDefault )
        {
            return !DisallowFeatures.Contains( featureName );
        }
        return AllowFeatures.Contains( featureName );
    }
}
