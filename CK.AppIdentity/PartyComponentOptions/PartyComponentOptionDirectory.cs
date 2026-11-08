using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The options of the party component owner types (<see cref="IPartyComponentOption{TOwner}"/>), computed by the
/// engine: for each owner type, its options (and the options of its base types) in their order, with their
/// relations and the code that creates them. No runtime reflection.
/// <para>
/// An owner selects its options from its configuration section (<see cref="TrySelect"/>), then creates and applies the
/// selected ones (<see cref="PartyComponentOptionInfo.Create(IServiceProvider)"/>), in this order.
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
    /// Selects the options of an owner from its configuration section: see
    /// <see cref="TrySelect(IActivityMonitor, IReadOnlyList{PartyComponentOptionInfo}, ImmutableConfigurationSection?, out IReadOnlyList{SelectedPartyComponentOption})"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="ownerType">The owner type.</param>
    /// <param name="ownerSection">The configuration section of the owner. Null when the owner has none.</param>
    /// <param name="selected">The selected options, in their order.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool TrySelect( IActivityMonitor monitor,
                           Type ownerType,
                           ImmutableConfigurationSection? ownerSection,
                           out IReadOnlyList<SelectedPartyComponentOption> selected )
    {
        return TrySelect( monitor, GetOptions( ownerType ), ownerSection, out selected );
    }

    /// <summary>
    /// Selects options from the configuration section of their owner. In the section, the key of an option is its name:
    /// <list type="bullet">
    ///   <item>a section (<c>"Name": { ... }</c>) or <c>true</c> enables it explicitly;</item>
    ///   <item><c>false</c> disables it;</item>
    ///   <item>nothing: its default (<see cref="PartyComponentOptionInfo.IsEnabledByDefault"/>).</item>
    /// </list>
    /// An option enabled explicitly that requires a disabled one is an error. An option enabled by default that requires a
    /// disabled one is disabled with it (an information is logged). This is a pure function of the options and of the
    /// configuration.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="options">The ordered options of the owner.</param>
    /// <param name="ownerSection">The configuration section of the owner. Null when the owner has none.</param>
    /// <param name="selected">The selected options, in their order.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool TrySelect( IActivityMonitor monitor,
                                  IReadOnlyList<PartyComponentOptionInfo> options,
                                  ImmutableConfigurationSection? ownerSection,
                                  out IReadOnlyList<SelectedPartyComponentOption> selected )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( options );
        selected = [];
        bool success = true;
        var enabled = new Dictionary<string, (PartyComponentOptionInfo Info, bool IsExplicit, ImmutableConfigurationSection? Section)>( StringComparer.OrdinalIgnoreCase );
        foreach( var o in options )
        {
            var s = ownerSection?.TryGetSection( o.Name );
            if( s == null )
            {
                if( o.IsEnabledByDefault ) enabled.Add( o.Name, (o, false, null) );
            }
            else if( s.Value != null )
            {
                if( bool.TryParse( s.Value, out var b ) )
                {
                    if( b ) enabled.Add( o.Name, (o, true, null) );
                }
                else
                {
                    monitor.Error( $"Invalid option '{s.Path}' = '{s.Value}': true, false or a section is expected." );
                    success = false;
                }
            }
            else
            {
                enabled.Add( o.Name, (o, true, s) );
            }
        }
        if( !success ) return false;
        // The closure: an option needs the options it requires.
        bool changed;
        do
        {
            changed = false;
            foreach( var (name, e) in enabled.ToList() )
            {
                var missing = e.Info.Requires.FirstOrDefault( r => !enabled.ContainsKey( r ) );
                if( missing == null ) continue;
                if( e.IsExplicit )
                {
                    monitor.Error( $"The option '{name}' requires the option '{missing}' that is disabled." );
                    success = false;
                }
                else
                {
                    monitor.Info( $"The option '{name}' (enabled by default) is disabled: it requires the option '{missing}' that is disabled." );
                }
                enabled.Remove( name );
                changed = true;
            }
        }
        while( changed );
        if( !success ) return false;
        selected = options.Where( o => enabled.ContainsKey( o.Name ) )
                          .Select( o => new SelectedPartyComponentOption( o, enabled[o.Name].Section ) )
                          .ToArray();
        return true;
    }
}

/// <summary>
/// A selected option of an owner: its description and its configuration section (null when it is enabled by default
/// or by <c>true</c>).
/// </summary>
/// <param name="Info">The option.</param>
/// <param name="Configuration">Its configuration section, if any.</param>
public readonly record struct SelectedPartyComponentOption( PartyComponentOptionInfo Info, ImmutableConfigurationSection? Configuration );
