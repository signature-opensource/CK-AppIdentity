using CK.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CK.AppIdentity;

/// <summary>
/// A tenant template: an entry of "Parties" with "InstanceCount", "Instances" or "MaxInstanceCount" (see
/// <see cref="ConfigurationTemplate"/>) whose party name is the last part of its domain (the rule of a tenant domain, on
/// the names with their placeholders): one tenant domain per instance. The qualifier of its placeholders is
/// <see cref="QualifierName"/> ("{Tenant.Name}", "{Tenant.Index}") in its inner templates.
/// <para>
/// A Closed template is expanded at start: its instances are configured tenant domains. An Opened one
/// (<see cref="ConfigurationTemplateInstances.IsOpened"/>) is kept (<see cref="ApplicationIdentityServiceConfiguration.TenantTemplates"/>):
/// its configured instances are expanded, and its run-time instances are created by the application, each one named by its
/// creator (<see cref="ConfigurationTemplateInstances.CreateDynamic(int, string)"/>): its party is <c>{DomainPrefix}/{Name}/${Name}</c>.
/// </para>
/// <para>
/// An Opened tenant template is written with a <c>"DomainName"</c> that ends with <c>"/{Name}"</c> and the
/// <c>"PartyName"</c> <c>"${Name}"</c> (no "FullName"): the template owns its <see cref="DomainPrefix"/>, no other party of
/// the System has its domain directly under it.
/// </para>
/// </summary>
public sealed class TenantDomainTemplate
{
    /// <summary>
    /// The qualifier of the placeholders of a tenant template in its inner templates: "Tenant" ("{Tenant.Index}").
    /// </summary>
    public const string QualifierName = "Tenant";

    // A placeholder of the index of the tenant: "{Tenant.Index}" or "{n+Tenant.Index}".
    static readonly Regex _tenantIndex = new Regex( @"\{(?:-?\d+\+)?Tenant\.Index\}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );

    readonly ImmutableConfigurationSection _configuration;
    readonly ImmutableConfigurationSection _partiesSection;
    readonly ConfigurationTemplateInstances _instances;
    readonly string _domainPrefix;
    readonly string _partyNamePattern;
    readonly string _inheritedDomainName;
    readonly string _environmentName;

    TenantDomainTemplate( ImmutableConfigurationSection configuration,
                          ImmutableConfigurationSection partiesSection,
                          ConfigurationTemplateInstances instances,
                          string domainPrefix,
                          string partyNamePattern,
                          string inheritedDomainName,
                          string environmentName )
    {
        _configuration = configuration;
        _partiesSection = partiesSection;
        _instances = instances;
        _domainPrefix = domainPrefix;
        _partyNamePattern = partyNamePattern;
        _inheritedDomainName = inheritedDomainName;
        _environmentName = environmentName;
    }

    /// <summary>
    /// Gets the unresolved configuration of the template.
    /// </summary>
    public ImmutableConfigurationSection Configuration => _configuration;

    /// <summary>
    /// Gets the configured instances and the bound of the run-time ones.
    /// </summary>
    public ConfigurationTemplateInstances Instances => _instances;

    /// <summary>
    /// Gets the domain under which the tenant domains of this template are: "Sample/Auth3/Customers" for the domain
    /// "Sample/Auth3/Customers/{Name}". It names the template in its application (and in its System: see
    /// <see cref="TenantDomainTemplate"/>).
    /// </summary>
    public string DomainPrefix => _domainPrefix;

    /// <summary>
    /// Gets the party name of the template, with its placeholders: "${Name}". It names the type set of its tenant domains
    /// (one set per template): a tenant domain of this template uses it, never a set found by its own name.
    /// </summary>
    public string PartyNamePattern => _partyNamePattern;

    /// <summary>
    /// Gets the environment of the tenant domains of this template.
    /// </summary>
    public string EnvironmentName => _environmentName;

    /// <summary>
    /// Gets the full name of the tenant domain of an instance name: <c>{DomainPrefix}/{name}/${name}/{EnvironmentName}</c>.
    /// </summary>
    /// <param name="name">The name of the instance.</param>
    /// <returns>The full name of its tenant domain.</returns>
    public NormalizedPath GetFullName( string name ) => $"{_domainPrefix}/{name}/${name}/{_environmentName}";

    /// <summary>
    /// Creates the configuration of an instance of this template: its placeholders resolved.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="instance">The instance (a configured or a run-time one).</param>
    /// <returns>The tenant domain configuration or null on error.</returns>
    public TenantDomainPartyConfiguration? CreateInstance( IActivityMonitor monitor, ConfigurationInstance instance )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( instance );
        var collector = new ApplicationIdentityPartyConfiguration.ProcessedConfiguration( new List<TenantDomainPartyConfiguration>(),
                                                                                          new List<RemotePartyConfiguration>(),
                                                                                          new List<RemotePartyTemplate>(),
                                                                                          new List<TenantDomainTemplate>() );
        var fullNameIndex = new Dictionary<string, ImmutableConfigurationSection>( StringComparer.OrdinalIgnoreCase );
        if( !ReadInstance( monitor, instance, ref collector, fullNameIndex ) ) return null;
        return collector.Tenants[^1];
    }

    bool ReadInstance( IActivityMonitor monitor,
                       ConfigurationInstance instance,
                       ref ApplicationIdentityPartyConfiguration.ProcessedConfiguration collector,
                       Dictionary<string, ImmutableConfigurationSection> fullNameIndex )
    {
        var section = ConfigurationTemplate.Instantiate( monitor, _configuration, instance, _partiesSection );
        if( section == null ) return false;
        var local = new ApplicationIdentityPartyConfiguration.ProcessedConfiguration( new List<TenantDomainPartyConfiguration>(),
                                                                                      new List<RemotePartyConfiguration>(),
                                                                                      new List<RemotePartyTemplate>(),
                                                                                      new List<TenantDomainTemplate>() );
        if( !ApplicationIdentityServiceConfiguration.ReadParties( monitor, section, _inheritedDomainName, _environmentName, ref local, fullNameIndex ) )
        {
            return false;
        }
        if( local.Tenants.Count != 1 || local.Remotes.Count > 0 || local.TenantTemplates.Count > 0 )
        {
            monitor.Error( $"The {instance} of the tenant template '{_configuration.Path}' must be a single tenant domain (its \"Parties\" are its remotes)." );
            return false;
        }
        var tenant = local.Tenants[0];
        var expected = GetFullName( instance.Name );
        if( _instances.IsOpened && !tenant.FullName.Path.Equals( expected.Path, StringComparison.OrdinalIgnoreCase ) )
        {
            monitor.Error( $"The {instance} of the tenant template '{_configuration.Path}' is '{tenant.FullName}': '{expected}' is expected." );
            return false;
        }
        tenant.SetInstance( this, instance );
        collector.Tenants.Add( tenant );
        return true;
    }

    // The names written by a party section, with their placeholders: its "DomainName" and "PartyName", or the parts of
    // its "FullName" (a template's names have placeholders: they are not parsed as a full name).
    static (string? Domain, string? Party) RawNames( IConfigurationSection s )
    {
        var full = s["FullName"];
        if( full != null )
        {
            var parts = full.Split( '/' );
            int iParty = Array.FindIndex( parts, p => p.StartsWith( '$' ) );
            if( iParty <= 0 ) return (null, null);
            return (string.Join( '/', parts, 0, iParty ), parts[iParty]);
        }
        var party = s["PartyName"];
        if( party != null && party.Length > 0 && party[0] != '$' ) party = '$' + party;
        return (s["DomainName"], party);
    }

    /// <summary>
    /// Gets whether a template section is a tenant template: its party name (with its placeholders) is the last part of its
    /// domain and it has no "Address" (the rule of a tenant domain, on the names with their placeholders).
    /// </summary>
    /// <param name="section">The template section.</param>
    /// <returns>True for a tenant template.</returns>
    public static bool IsTenantTemplate( IConfigurationSection section )
    {
        Throw.CheckNotNullArgument( section );
        if( section["Address"] != null ) return false;
        var (domain, party) = RawNames( section );
        if( domain == null || party == null ) return false;
        var last = domain.AsSpan( domain.LastIndexOf( '/' ) + 1 );
        return party.AsSpan( 1 ).Equals( last, StringComparison.OrdinalIgnoreCase );
    }

    // Reads a tenant template entry of "Parties": its configured instances are read into the collector; an Opened one is
    // also collected (its first run-time instance is checked).
    internal static bool Read( IActivityMonitor monitor,
                               ImmutableConfigurationSection partiesSection,
                               ImmutableConfigurationSection configuration,
                               ConfigurationTemplateInstances instances,
                               string domainName,
                               string environmentName,
                               ref ApplicationIdentityPartyConfiguration.ProcessedConfiguration collector,
                               Dictionary<string, ImmutableConfigurationSection> fullNameIndex )
    {
        using var gLog = monitor.OpenInfo( instances.IsOpened
                                            ? $"Tenant template '{configuration.Path}': {instances.Count} configured instance(s), at most {instances.MaxInstanceCount}."
                                            : $"Tenant template '{configuration.Path}': {instances.Count} instance(s)." );
        bool success = CheckInnerParties( monitor, configuration );
        var (rawDomain, rawParty) = RawNames( configuration );
        Throw.DebugAssert( rawDomain != null && rawParty != null );
        string domainPrefix = rawDomain.Contains( '/' ) ? rawDomain.Substring( 0, rawDomain.LastIndexOf( '/' ) ) : string.Empty;
        if( instances.IsOpened )
        {
            // The names of the run-time instances are free: the domain ends with the name, the party is the name.
            if( configuration["FullName"] != null
                || !rawDomain.EndsWith( "/{Name}", StringComparison.OrdinalIgnoreCase )
                || !rawParty.Equals( "${Name}", StringComparison.OrdinalIgnoreCase ) )
            {
                monitor.Error( $"The Opened tenant template '{configuration.Path}' must have a \"DomainName\" that ends with \"/{{Name}}\" and the \"PartyName\" \"${{Name}}\" (not a \"FullName\"): its run-time instances are named by their creator." );
                return false;
            }
            if( !CoreApplicationIdentity.IsValidDomainName( domainPrefix ) )
            {
                monitor.Error( $"The domain of the Opened tenant template '{configuration.Path}' must be under a valid domain: '{domainPrefix}' is not." );
                return false;
            }
        }
        var template = new TenantDomainTemplate( configuration, partiesSection, instances, domainPrefix, rawParty, domainName, environmentName );
        foreach( var i in instances )
        {
            success &= template.ReadInstance( monitor, i, ref collector, fullNameIndex );
        }
        if( !instances.IsOpened ) return success;
        // The run-time instances differ only by their index and their name: the first one is checked.
        if( instances.MaxInstanceCount > instances.Count )
        {
            var check = instances.CreateDynamic( instances.Count + 1, instances.IsConfiguredName( "DynamicCheck" ) ? "DynamicCheckTenant" : "DynamicCheck" );
            if( template.CreateInstance( monitor, check ) == null )
            {
                monitor.Error( $"Invalid run-time instance of the tenant template '{configuration.Path}'." );
                success = false;
            }
        }
        if( success ) collector.TenantTemplates.Add( template );
        return success;
    }

    // The parties of a tenant template: a tenant template inside it is an error (one level of tenant templates), and the
    // name of a party can't use the index of the tenant (it changes with SetTenantIndex: the party would change).
    static bool CheckInnerParties( IActivityMonitor monitor, IConfigurationSection template )
    {
        bool success = true;
        foreach( var p in template.GetSection( "Parties" ).GetChildren() )
        {
            success &= Check( monitor, p );
        }
        return success;

        static bool Check( IActivityMonitor monitor, IConfigurationSection p )
        {
            bool success = true;
            if( ConfigurationTemplate.IsTemplate( p ) && IsTenantTemplate( p ) )
            {
                monitor.Error( $"'{p.Path}' is a tenant template inside a tenant template: a tenant template can't contain another one." );
                success = false;
            }
            foreach( var key in (string[])["FullName", "DomainName", "PartyName"] )
            {
                var v = p[key];
                if( v != null && _tenantIndex.IsMatch( v ) )
                {
                    monitor.Error( $"'{p.Path}:{key}' uses the index of the tenant ('{v}'): a party name can't change with the index of its tenant. Use \"{{Tenant.Name}}\"." );
                    success = false;
                }
            }
            foreach( var c in p.GetSection( "Parties" ).GetChildren() )
            {
                success &= Check( monitor, c );
            }
            return success;
        }
    }

    /// <summary>
    /// Overridden to return the domain prefix and the path of the template.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"'{_domainPrefix}/{{Name}}' ({_configuration.Path})";
}
