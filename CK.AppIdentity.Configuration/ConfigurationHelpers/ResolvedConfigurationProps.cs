using CK.Core;
using System.Collections.Immutable;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The combinable properties of a party configuration (a "Local" section or a remote): the levels where its feature
/// declarations are looked up and its assemblies. They are resolved from the party's own keys and the "Defaults"
/// sections above the party: the nearest level wins. A structural level (the root, a tenant domain, a group) has none:
/// <see cref="Empty"/>.
/// </summary>
readonly struct ResolvedConfigurationProps
{
    /// <summary>
    /// The party section, then the "Defaults" sections above it, the nearest first.
    /// </summary>
    public readonly ImmutableArray<ImmutableConfigurationSection> FeatureLevels;
    public readonly AssemblyConfiguration AssemblyConfiguration;

    ResolvedConfigurationProps( ImmutableArray<ImmutableConfigurationSection> featureLevels,
                                AssemblyConfiguration assemblyConfiguration )
    {
        FeatureLevels = featureLevels;
        AssemblyConfiguration = assemblyConfiguration;
    }

    /// <summary>
    /// The properties of a structural level.
    /// </summary>
    public static ResolvedConfigurationProps Empty => new ResolvedConfigurationProps( [], AssemblyConfiguration.Empty );

    /// <summary>
    /// Resolves the properties of a party configuration section.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="party">The party configuration (a "Local" section or a remote).</param>
    /// <param name="props">The resolved properties.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryResolve( IActivityMonitor monitor, ImmutableConfigurationSection party, out ResolvedConfigurationProps props )
    {
        // The party itself, then the "Defaults" above it (the nearest first).
        var levels = party.LookupAllSectionWithDistance( ConfigurationSectionExtension.DefaultsKey )
                          .Where( d => d.Item2 > 0 )
                          .OrderBy( d => d.Item2 )
                          .Select( d => d.Item1 )
                          .Prepend( party )
                          .ToImmutableArray();
        var assemblies = AssemblyConfiguration.Resolve( monitor, party );
        if( assemblies != null )
        {
            props = new ResolvedConfigurationProps( levels, assemblies );
            return true;
        }
        props = Empty;
        return false;
    }
}
