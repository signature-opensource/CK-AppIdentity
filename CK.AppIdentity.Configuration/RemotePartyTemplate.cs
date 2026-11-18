using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// A remote template: an entry of "Parties" with "InstanceCount", "Instances" or "MaxInstanceCount" (see
/// <see cref="ConfigurationTemplate"/>), one remote per instance. Its configured instances are expanded at start (they
/// are in <see cref="ApplicationIdentityServiceConfiguration.Remotes"/> or <see cref="TenantDomainPartyConfiguration.Remotes"/>,
/// with this template as their <see cref="RemotePartyConfiguration.Template"/>).
/// <para>
/// An Opened template (<see cref="ConfigurationTemplateInstances.IsOpened"/>) also accepts dynamic instances, numbered
/// after the configured ones up to <see cref="ConfigurationTemplateInstances.MaxInstanceCount"/> and named by their
/// index: an unknown caller whose full name matches the template is a candidate (it is trusted only by an approval), and
/// a dynamic instance whose identity is pinned in the store is created again at start.
/// </para>
/// </summary>
public sealed class RemotePartyTemplate
{
    readonly ImmutableConfigurationSection _configuration;
    readonly ImmutableConfigurationSection _partiesSection;
    readonly ConfigurationTemplateInstances _instances;
    readonly string _partyNamePattern;
    readonly string _domainName;
    readonly string _environmentName;

    RemotePartyTemplate( ImmutableConfigurationSection configuration,
                         ImmutableConfigurationSection partiesSection,
                         ConfigurationTemplateInstances instances,
                         string partyNamePattern,
                         string domainName,
                         string environmentName )
    {
        _configuration = configuration;
        _partiesSection = partiesSection;
        _instances = instances;
        _partyNamePattern = partyNamePattern;
        _domainName = domainName;
        _environmentName = environmentName;
    }

    /// <summary>
    /// Gets the unresolved configuration of the template.
    /// </summary>
    public ImmutableConfigurationSection Configuration => _configuration;

    /// <summary>
    /// Gets the configured instances and the bound of the dynamic ones.
    /// </summary>
    public ConfigurationTemplateInstances Instances => _instances;

    /// <summary>
    /// Gets the party name of the template, with its placeholders: "$Worker-{Name}".
    /// </summary>
    public string PartyNamePattern => _partyNamePattern;

    /// <summary>
    /// Gets whether an instance is a dynamic one: its index is above the configured instances.
    /// </summary>
    /// <param name="instance">The instance.</param>
    /// <returns>True for a dynamic instance.</returns>
    public bool IsDynamic( ConfigurationInstance instance ) => _instances.IsDynamicIndex( instance.Index );

    /// <summary>
    /// Finds the dynamic instance that a full name names (its party name is the <see cref="PartyNamePattern"/> resolved
    /// by a dynamic index within the bound). The configuration of the instance (<see cref="CreateInstance"/>) must then
    /// have this full name.
    /// </summary>
    /// <param name="fullName">The full name of a party.</param>
    /// <returns>The dynamic instance or null.</returns>
    public ConfigurationInstance? MatchDynamic( NormalizedPath fullName )
    {
        if( !_instances.IsOpened || fullName.Parts.Count < 3 ) return null;
        return _instances.MatchDynamic( _partyNamePattern, fullName.Parts[^2] );
    }

    /// <summary>
    /// Creates the configuration of an instance of this template: its placeholders resolved.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="instance">The instance (a configured or a dynamic one).</param>
    /// <returns>The remote configuration or null on error.</returns>
    public RemotePartyConfiguration? CreateInstance( IActivityMonitor monitor, ConfigurationInstance instance )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( instance );
        var collector = new ApplicationIdentityPartyConfiguration.ProcessedConfiguration( new List<TenantDomainPartyConfiguration>(),
                                                                                          new List<RemotePartyConfiguration>(),
                                                                                          new List<RemotePartyTemplate>() );
        var fullNameIndex = new Dictionary<string, ImmutableConfigurationSection>( StringComparer.OrdinalIgnoreCase );
        if( !ReadInstance( monitor, instance, ref collector, fullNameIndex ) ) return null;
        if( collector.Tenants.Count > 0 || collector.Remotes.Count != 1 )
        {
            monitor.Error( $"The {instance} of the template '{_configuration.Path}' must be a single remote party." );
            return null;
        }
        return collector.Remotes[0];
    }

    bool ReadInstance( IActivityMonitor monitor,
                       ConfigurationInstance instance,
                       ref ApplicationIdentityPartyConfiguration.ProcessedConfiguration collector,
                       Dictionary<string, ImmutableConfigurationSection> fullNameIndex )
    {
        var section = ConfigurationTemplate.Instantiate( monitor, _configuration, instance, _partiesSection );
        if( section == null ) return false;
        int remoteCount = collector.Remotes.Count;
        if( !ApplicationIdentityServiceConfiguration.ReadParties( monitor, section, _domainName, _environmentName, ref collector, fullNameIndex ) )
        {
            return false;
        }
        for( int i = remoteCount; i < collector.Remotes.Count; ++i )
        {
            collector.Remotes[i].SetInstance( this, instance );
        }
        return true;
    }

    // Reads a template entry of "Parties": its configured instances are read into the collector; an Opened remote
    // template is also collected (its first dynamic instance is checked).
    internal static bool Read( IActivityMonitor monitor,
                               ImmutableConfigurationSection partiesSection,
                               ImmutableConfigurationSection configuration,
                               ConfigurationTemplateInstances instances,
                               string domainName,
                               string environmentName,
                               ref ApplicationIdentityPartyConfiguration.ProcessedConfiguration collector,
                               Dictionary<string, ImmutableConfigurationSection> fullNameIndex )
    {
        var pattern = RawPartyName( configuration );
        if( pattern == null || !ConfigurationTemplate.HasPlaceholder( pattern ) )
        {
            monitor.Error( $"The party name '{pattern}' of the template '{configuration.Path}' must use the \"{{Name}}\" or \"{{Index}}\" placeholder: its instances are different parties." );
            return false;
        }
        var template = new RemotePartyTemplate( configuration, partiesSection, instances, pattern, domainName, environmentName );
        using var gLog = monitor.OpenInfo( instances.IsOpened
                                            ? $"Party template '{configuration.Path}': {instances.Count} configured instance(s), at most {instances.MaxInstanceCount}."
                                            : $"Party template '{configuration.Path}': {instances.Count} instance(s)." );
        int tenantCount = collector.Tenants.Count;
        bool success = true;
        foreach( var i in instances )
        {
            success &= template.ReadInstance( monitor, i, ref collector, fullNameIndex );
        }
        if( !instances.IsOpened ) return success;
        if( collector.Tenants.Count > tenantCount )
        {
            monitor.Error( $"The template '{configuration.Path}' is an Opened tenant template (\"{ConfigurationTemplate.MaxInstanceCountKey}\"): only a remote template can be Opened." );
            return false;
        }
        // The dynamic instances differ only by their index: the first one is checked.
        if( instances.MaxInstanceCount > instances.Count
            && template.CreateInstance( monitor, instances.CreateDynamic( instances.Count + 1 ) ) == null )
        {
            monitor.Error( $"Invalid dynamic instance of the template '{configuration.Path}'." );
            return false;
        }
        if( success ) collector.RemoteTemplates.Add( template );
        return success;
    }

    // The party name written by a party section: its "PartyName", or the party of its "FullName" (a template's name
    // has placeholders: it is not parsed as a full name).
    static string? RawPartyName( ImmutableConfigurationSection s )
    {
        var p = s["PartyName"];
        if( !string.IsNullOrWhiteSpace( p ) ) return p[0] == '$' ? p : '$' + p;
        return s["FullName"]?.Split( '/' ).FirstOrDefault( part => part.StartsWith( '$' ) );
    }

    /// <summary>
    /// Overridden to return the path of the template and its party name.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"'{_partyNamePattern}' ({_configuration.Path})";
}
