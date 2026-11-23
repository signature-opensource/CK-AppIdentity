using CK.Core;
using CK.Setup;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The options of the party component owner types (<see cref="IPartyComponentOption{TOwner}"/>), computed by the
/// engine: for each owner type, its options (and the options of its base types) in their order, with their
/// relations and the code that creates them. No runtime reflection.
/// <para>
/// The options of an owner are selected from its configuration section and created (<see cref="TryCreate(IActivityMonitor, Type, ImmutableConfigurationSection?, IServiceProvider, out IReadOnlyList{object})"/>),
/// in their order: an option reads its configuration in its constructor (an <see cref="IConfiguration"/> parameter). A
/// feature driver holds the options of each party (see <c>ApplicationIdentityFeatureDriver.GetOptions</c>), a client
/// feature its own ones.
/// </para>
/// </summary>
[ContextBoundDelegation( "CK.Setup.PartyComponentOptionDirectoryImpl, CK.AppIdentity.Engine" )]
public abstract class PartyComponentOptionDirectory : ISingletonAutoService
{
    readonly IReadOnlyDictionary<Type, IReadOnlyList<PartyComponentOptionInfo>> _options;

    /// <summary>
    /// Initializes a new directory. Called by the generated code.
    /// </summary>
    /// <param name="options">The ordered options of each owner type.</param>
    protected PartyComponentOptionDirectory( IReadOnlyDictionary<Type, IReadOnlyList<PartyComponentOptionInfo>> options )
    {
        _options = options;
    }

    /// <summary>
    /// Gets the owner types that have options.
    /// </summary>
    public IEnumerable<Type> OwnerTypes => _options.Keys;

    /// <summary>
    /// Gets the options of an owner type, in their order. Empty when the owner type has no option.
    /// </summary>
    /// <param name="ownerType">The owner type (the actual type of the owner).</param>
    /// <returns>The ordered options.</returns>
    public IReadOnlyList<PartyComponentOptionInfo> GetOptions( Type ownerType ) => _options.GetValueOrDefault( ownerType ) ?? [];

    /// <summary>
    /// Gets whether a configuration key of an owner section is the name of one of its options.
    /// </summary>
    /// <param name="ownerType">The owner type.</param>
    /// <param name="key">The configuration key.</param>
    /// <returns>True if the key is an option name.</returns>
    public bool IsOptionName( Type ownerType, string key ) => GetOptions( ownerType ).Any( o => StringComparer.OrdinalIgnoreCase.Equals( o.Name, key ) );

    /// <summary>
    /// Selects the options of an owner from its configuration section (see <see cref="ConfigurationOptionSelector"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="ownerType">The owner type.</param>
    /// <param name="ownerSection">The configuration section of the owner. Null when the owner has none.</param>
    /// <param name="selected">The selected options, in their order.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool TrySelect( IActivityMonitor monitor,
                           Type ownerType,
                           ImmutableConfigurationSection? ownerSection,
                           out IReadOnlyList<SelectedConfigurationOption<PartyComponentOptionInfo>> selected )
    {
        return ConfigurationOptionSelector.TrySelect( monitor, GetOptions( ownerType ), ownerSection, out selected );
    }

    /// <summary>
    /// Selects the options of an owner from its configuration section and creates them, in their order (see
    /// <see cref="PartyComponentOptionInfo.Create(IServiceProvider, IConfiguration?)"/>). An option that can't be created
    /// (an invalid configuration throws) is an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="ownerType">The owner type.</param>
    /// <param name="ownerSection">The configuration section of the owner. Null when the owner has none.</param>
    /// <param name="services">The owner's services.</param>
    /// <param name="created">The created options, in their order.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool TryCreate( IActivityMonitor monitor,
                           Type ownerType,
                           ImmutableConfigurationSection? ownerSection,
                           IServiceProvider services,
                           out IReadOnlyList<object> created )
    {
        return TryCreate( monitor, GetOptions( ownerType ), name => [ownerSection?.TryGetSection( name )], ownerSection?.Path, services, out created );
    }

    /// <summary>
    /// Selects options and creates them: see <see cref="TryCreate(IActivityMonitor, Type, ImmutableConfigurationSection?, IServiceProvider, out IReadOnlyList{object})"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="options">The ordered options of the owner.</param>
    /// <param name="findOptionLevels">
    /// Finds the configuration of an option by its name at every level, the nearest first (its section in the owner's
    /// section, then in the "Defaults" above it; null where a level has none). The selection takes the nearest one; a
    /// configuration model is read key by key along them.
    /// </param>
    /// <param name="ownerPath">The configuration path of the owner (for the errors).</param>
    /// <param name="services">The owner's services.</param>
    /// <param name="created">The created options, in their order.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool TryCreate( IActivityMonitor monitor,
                                  IReadOnlyList<PartyComponentOptionInfo> options,
                                  Func<string, IReadOnlyList<ImmutableConfigurationSection?>> findOptionLevels,
                                  string? ownerPath,
                                  IServiceProvider services,
                                  out IReadOnlyList<object> created )
    {
        created = [];
        if( !ConfigurationOptionSelector.TrySelect( monitor,
                                                    options,
                                                    name => findOptionLevels( name ).FirstOrDefault( s => s != null ),
                                                    out var selected ) )
        {
            return false;
        }
        var result = new object[selected.Count];
        bool success = true;
        for( int i = 0; i < result.Length; i++ )
        {
            var (info, configuration) = selected[i];
            object? arg = configuration;
            if( info.Model != null )
            {
                // The model is read key by key along the levels (a bare "true" has no section at its level).
                var levels = findOptionLevels( info.Name ).Select( s => s != null && s.Value == null ? s : null ).ToArray();
                var collector = new UserMessageCollector( new CurrentCultureInfo( new TranslationService(), NormalizedCultureInfo.CodeDefault ) );
                arg = ConfigurationModelReader.TryRead( info.Model,
                                                        levels,
                                                        configuration?.Path ?? (ownerPath != null ? $"{ownerPath}:{info.Name}" : info.Name),
                                                        collector );
                collector.DumpLogs( monitor );
                if( arg == null )
                {
                    success = false;
                    continue;
                }
            }
            try
            {
                result[i] = info.Create( services, arg );
            }
            catch( Exception ex )
            {
                var path = configuration?.Path ?? (ownerPath != null ? $"{ownerPath}:{info.Name}" : info.Name);
                monitor.Error( $"Unable to create the option '{path}': {ex.Message}", ex );
                success = false;
            }
        }
        if( success ) created = result;
        return success;
    }
}
