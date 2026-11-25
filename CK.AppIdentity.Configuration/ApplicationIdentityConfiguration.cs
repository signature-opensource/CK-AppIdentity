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
        return GetFeatureDeclarationSection( featureName ) is { } s
                ? !(s.Value != null && string.Equals( s.Value, "false", StringComparison.OrdinalIgnoreCase ))
                : null;
    }

    /// <summary>
    /// Gets the key that declares a feature for this party: the one of the party, otherwise of the nearest "Defaults"
    /// above it. Its <see cref="ImmutableConfigurationSection.Value"/> is <c>true</c> or <c>false</c>, or it is the
    /// feature's section. Null when the feature is not declared.
    /// </summary>
    /// <param name="featureName">The feature name.</param>
    /// <returns>The declaring key or null.</returns>
    public ImmutableConfigurationSection? GetFeatureDeclarationSection( string featureName )
    {
        foreach( var level in _featureLevels )
        {
            if( level.TryGetSection( featureName ) is { } s ) return s;
        }
        return null;
    }

    /// <summary>
    /// Gets a section of the configuration of a feature (a setting or an option of the feature): the
    /// <c>{featureName}:{path}</c> section of the party, otherwise of the nearest "Defaults" above it.
    /// </summary>
    /// <param name="featureName">The feature name (its key).</param>
    /// <param name="path">The path in the feature's section.</param>
    /// <returns>The section, null when it is not configured.</returns>
    public ImmutableConfigurationSection? TryGetFeatureSection( string featureName, string path )
    {
        foreach( var level in _featureLevels )
        {
            if( level.TryGetSection( $"{featureName}:{path}" ) is { } s ) return s;
        }
        return null;
    }

    /// <summary>
    /// Gets the section of a feature at every level: the <c>{featureName}</c> section of the party, then of each "Defaults"
    /// above it, the nearest first (null where a level has none, or declares it by a value: <c>true</c>). The configuration
    /// model of the feature is read key by key along them.
    /// </summary>
    /// <param name="featureName">The feature name (its key).</param>
    /// <returns>The sections, the nearest first.</returns>
    public IReadOnlyList<ImmutableConfigurationSection?> GetFeatureLevels( string featureName )
    {
        var result = new ImmutableConfigurationSection?[_featureLevels.Length];
        for( int i = 0; i < result.Length; i++ )
        {
            var s = _featureLevels[i].TryGetSection( featureName );
            result[i] = s != null && s.Value == null ? s : null;
        }
        return result;
    }

    /// <summary>
    /// Gets the sections of the configuration of a feature (a setting or an option of the feature) at every level: the
    /// <c>{featureName}:{path}</c> section of the party, then of each "Defaults" above it, the nearest first (null where a
    /// level has none). A configuration model is read key by key along them.
    /// </summary>
    /// <param name="featureName">The feature name (its key).</param>
    /// <param name="path">The path in the feature's section.</param>
    /// <returns>The sections, the nearest first.</returns>
    public IReadOnlyList<ImmutableConfigurationSection?> GetFeatureSections( string featureName, string path )
    {
        var result = new ImmutableConfigurationSection?[_featureLevels.Length];
        for( int i = 0; i < result.Length; i++ ) result[i] = _featureLevels[i].TryGetSection( $"{featureName}:{path}" );
        return result;
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
    /// Gets the keys of this party and of the "Defaults" above it that are not keys of the party: the keys of a party
    /// are its own ones (<see cref="IsPartyKey(string)"/>) or features (<c>true</c>, <c>false</c> or the feature's
    /// section). A key here that names no feature is an error (a typo could leave a feature allowed by default on).
    /// </summary>
    /// <returns>The distinct keys (case insensitive).</returns>
    public IEnumerable<string> GetFeatureKeys()
    {
        if( _featureLevels.IsDefaultOrEmpty ) return [];
        return _featureLevels[0].GetChildren().Select( c => c.Key ).Where( k => !IsPartyKey( k ) )
                                .Concat( _featureLevels.Skip( 1 ).SelectMany( l => l.GetChildren() ).Select( c => c.Key ).Where( k => !StructuralKeys.DefaultsKeys.Contains( k ) ) )
                                .Distinct( StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets whether a key is a key of this party that is not a feature (<c>PublicUrl</c> of a local party, <c>Address</c>
    /// of a remote, the assemblies...).
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>True for a key of the party.</returns>
    public virtual bool IsPartyKey( string key ) => StructuralKeys.RemotePartyKeys.Contains( key );
}
