using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// The keys of the structural levels (the root, a tenant domain, a group): their identity and their structure only.
/// The configuration of a local party is in its "Local" section, the default values of the parties below a level in its
/// "Defaults" section: nothing is implicitly inherited from a level.
/// </summary>
static class StructuralKeys
{
    static readonly HashSet<string> _root = new( StringComparer.OrdinalIgnoreCase )
    {
        "FullName", "DomainName", "PartyName", "EnvironmentName", "Parties", "Local", "Defaults",
        "StoreRootPath", "TrashBinRetention", "StrictConfigurationMode"
    };

    static readonly HashSet<string> _tenant = new( StringComparer.OrdinalIgnoreCase )
    {
        "FullName", "DomainName", "PartyName", "EnvironmentName", "Parties", "Local", "Defaults"
    };

    static readonly HashSet<string> _group = new( StringComparer.OrdinalIgnoreCase )
    {
        "DomainName", "EnvironmentName", "Parties", "Defaults"
    };

    // The keys that cannot be default values: the identity and the structure of a level or of a party.
    static readonly HashSet<string> _notInDefaults = new( StringComparer.OrdinalIgnoreCase )
    {
        "FullName", "DomainName", "PartyName", "EnvironmentName", "Parties", "Local", "Defaults", "Address",
        "StoreRootPath", "TrashBinRetention", "StrictConfigurationMode"
    };

    /// <summary>
    /// Checks the root configuration.
    /// </summary>
    public static bool CheckRoot( IActivityMonitor monitor, ImmutableConfigurationSection root ) => Check( monitor, root, _root, "the root", hasLocal: true );

    /// <summary>
    /// Checks a tenant domain configuration.
    /// </summary>
    public static bool CheckTenant( IActivityMonitor monitor, ImmutableConfigurationSection tenant ) => Check( monitor, tenant, _tenant, "a tenant domain", hasLocal: true );

    /// <summary>
    /// Checks a group of parties.
    /// </summary>
    public static bool CheckGroup( IActivityMonitor monitor, ImmutableConfigurationSection group ) => Check( monitor, group, _group, "a group of parties", hasLocal: false );

    /// <summary>
    /// Checks a remote party: it is not a level, it has no "Local" and no "Defaults".
    /// </summary>
    public static bool CheckRemote( IActivityMonitor monitor, ImmutableConfigurationSection remote )
    {
        bool success = true;
        foreach( var key in new[] { "Local", "Defaults" } )
        {
            if( remote.TryGetSection( key ) is { } s )
            {
                monitor.Error( $"'{s.Path}' is not allowed: '{remote.Path}' is a remote party, not a level of the configuration (only the root and the tenant domains have a \"Local\", only the levels have \"Defaults\")." );
                success = false;
            }
        }
        return success;
    }

    static bool Check( IActivityMonitor monitor, ImmutableConfigurationSection level, HashSet<string> allowed, string kind, bool hasLocal )
    {
        bool success = true;
        foreach( var c in level.GetChildren() )
        {
            if( !allowed.Contains( c.Key ) )
            {
                monitor.Error( $"'{c.Path}' is not allowed: '{level.Path}' is {kind}, its keys are only its identity and its structure "
                               + $"({string.Join( ", ", allowed )}). Move it to "
                               + (hasLocal ? "the \"Local\" section (the configuration of the local party) or to " : "")
                               + "the \"Defaults\" section (the default values of the parties below)." );
                success = false;
            }
        }
        if( level.TryGetSection( ConfigurationSectionExtension.DefaultsKey ) is { } defaults )
        {
            foreach( var c in defaults.GetChildren() )
            {
                if( _notInDefaults.Contains( c.Key ) )
                {
                    monitor.Error( $"'{c.Path}' is not allowed: \"Defaults\" holds the default values of the parties, not an identity or a structure." );
                    success = false;
                }
            }
        }
        return success;
    }
}
