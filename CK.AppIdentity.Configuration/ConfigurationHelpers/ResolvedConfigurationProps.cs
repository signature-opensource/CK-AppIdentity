using CK.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The combinable properties of a party configuration (a "Local" section or a remote): the Allow/DisallowFeatures and the
/// assemblies. They are resolved from the "Defaults" sections above the party (from the root down) and the party's own
/// keys: the nearest level wins. A structural level (the root, a tenant domain, a group) has none: <see cref="Empty"/>.
/// </summary>
readonly struct ResolvedConfigurationProps
{
    public readonly IReadOnlySet<string> AllowFeatures;
    public readonly IReadOnlySet<string> DisallowFeatures;
    public readonly AssemblyConfiguration AssemblyConfiguration;

    ResolvedConfigurationProps( IReadOnlySet<string> allowFeatures,
                                IReadOnlySet<string> disallowFeatures,
                                AssemblyConfiguration assemblyConfiguration )
    {
        AllowFeatures = allowFeatures;
        DisallowFeatures = disallowFeatures;
        AssemblyConfiguration = assemblyConfiguration;
    }

    /// <summary>
    /// The properties of a structural level.
    /// </summary>
    public static ResolvedConfigurationProps Empty => new ResolvedConfigurationProps( ImmutableHashSet<string>.Empty,
                                                                                      ImmutableHashSet<string>.Empty,
                                                                                      AssemblyConfiguration.Empty );

    /// <summary>
    /// Resolves the properties of a party configuration section.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="party">The party configuration (a "Local" section or a remote).</param>
    /// <param name="props">The resolved properties.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryResolve( IActivityMonitor monitor, ImmutableConfigurationSection party, out ResolvedConfigurationProps props )
    {
        bool success = true;
        var allow = new HashSet<string>();
        var disallow = new HashSet<string>();
        // The "Defaults" above (from the root down: the nearest wins), then the party itself.
        var levels = party.LookupAllSectionWithDistance( ConfigurationSectionExtension.DefaultsKey )
                          .Where( d => d.Item2 > 0 )
                          .Select( d => d.Item1 )
                          .Append( party );
        foreach( var level in levels )
        {
            if( TryReadAllowDisallowFeatures( monitor, level, out var a, out var d ) )
            {
                allow.ExceptWith( d );
                allow.UnionWith( a );
                disallow.ExceptWith( a );
                disallow.UnionWith( d );
            }
            else
            {
                success = false;
            }
        }
        var assemblies = AssemblyConfiguration.Resolve( monitor, party );
        if( success && assemblies != null )
        {
            props = new ResolvedConfigurationProps( allow, disallow, assemblies );
            return true;
        }
        props = Empty;
        return false;
    }

    static bool TryReadAllowDisallowFeatures( IActivityMonitor monitor,
                                              ImmutableConfigurationSection level,
                                              [NotNullWhen( true )] out HashSet<string>? allow,
                                              [NotNullWhen( true )] out HashSet<string>? disallow )
    {
        allow = level.ReadUniqueStringSet( monitor, "AllowFeatures" );
        disallow = level.ReadUniqueStringSet( monitor, "DisallowFeatures" );
        if( allow != null && disallow != null )
        {
            if( !allow.Overlaps( disallow ) )
            {
                return true;
            }
            monitor.Error( $"The same feature cannot be both in '{level.Path}:AllowFeatures' and 'DisallowFeatures': {allow.Intersect( disallow ).Concatenate()}." );
        }
        return false;
    }
}
