using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// Base configuration class for <see cref="ApplicationIdentityPartyConfiguration"/> (root, remotes and tenants)
/// and <see cref="ApplicationIdentityLocalConfiguration"/>, the <see cref="ILocalParty.LocalConfiguration"/>.
/// <para>
/// The features and the assemblies are the ones of a party: a remote or a local party (the "Local" section of the root
/// or of a tenant domain). A party <b>declares a feature by its key</b>: <c>"AuthCenter": true</c> or its section
/// (<c>"HttpEndpoint": { ... }</c>); <c>false</c> turns off a feature that is allowed by default. The declarations are
/// looked up in the party's own keys, then in the "Defaults" sections above the party: the nearest level wins. The root
/// and the tenant domains are structural levels: they declare nothing, their local party is their
/// <see cref="ILocalParty.LocalConfiguration"/>.
/// </para>
/// </summary>
public class ApplicationIdentityConfiguration
{
    readonly ImmutableConfigurationSection _configuration;
    readonly ImmutableArray<ImmutableConfigurationSection> _featureLevels;
    readonly AssemblyConfiguration _assemblyConfiguration;

    internal ApplicationIdentityConfiguration( ImmutableConfigurationSection configuration,
                                               ref ResolvedConfigurationProps props )
    {
        _configuration = configuration;
        _featureLevels = props.FeatureLevels;
        _assemblyConfiguration = props.AssemblyConfiguration;
    }

    /// <summary>
    /// Gets the configuration section for this object.
    /// </summary>
    public ImmutableConfigurationSection Configuration => _configuration;

    /// <summary>
    /// Gets the <see cref="AssemblyConfiguration"/> that carries the external allowed assemblies
    /// from which plugins can be looked up for this party.
    /// </summary>
    public AssemblyConfiguration AssemblyConfiguration => _assemblyConfiguration;

    /// <summary>
    /// Gets the declaration of a feature by this party: true when its key is <c>true</c> or a section, false when it is
    /// <c>false</c>, null when the party (and the "Defaults" above it) don't declare it. The nearest level wins and the
    /// names are case insensitive.
    /// </summary>
    /// <param name="featureName">The feature name.</param>
    /// <returns>The declaration or null.</returns>
    public bool? GetFeatureDeclaration( string featureName )
    {
        foreach( var level in _featureLevels )
        {
            if( level.TryGetSection( featureName ) is { } s )
            {
                return !(s.Value != null && string.Equals( s.Value, "false", StringComparison.OrdinalIgnoreCase ));
            }
        }
        return null;
    }

    /// <summary>
    /// Computes whether a feature is allowed for this party: it is declared (see <see cref="GetFeatureDeclaration(string)"/>),
    /// or it is not turned off and <paramref name="isAllowedByDefault"/> is true.
    /// </summary>
    /// <param name="featureName">The feature name.</param>
    /// <param name="isAllowedByDefault">Whether this feature is allowed by default (an opt-out feature).</param>
    /// <returns>True if the feature is allowed for this party.</returns>
    public bool IsAllowedFeature( string featureName, bool isAllowedByDefault ) => GetFeatureDeclaration( featureName ) ?? isAllowedByDefault;

    /// <summary>
    /// Gets the keys of this party and of the "Defaults" above it whose value is <c>true</c> or <c>false</c>: the
    /// declarations of features that are not configured by a section. A key here that names no feature is a typo
    /// (that could leave a feature allowed by default on).
    /// </summary>
    /// <returns>The distinct keys (case insensitive).</returns>
    public IEnumerable<string> GetBooleanKeys()
    {
        return _featureLevels.SelectMany( l => l.GetChildren() )
                             .Where( c => c.Value != null
                                          && (string.Equals( c.Value, "true", StringComparison.OrdinalIgnoreCase )
                                              || string.Equals( c.Value, "false", StringComparison.OrdinalIgnoreCase )) )
                             .Select( c => c.Key )
                             .Distinct( StringComparer.OrdinalIgnoreCase );
    }
}
