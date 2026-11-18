using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.AppIdentity;

/// <summary>
/// A tenant domain in the <see cref="ApplicationIdentityServiceConfiguration"/>: the last part
/// of its DomainName is the same as its PartyName AND it has no "Address (otherwise it is a remote party).
/// Most often it defines <see cref="Remotes"/> but this is allowed to be empty to support fully dynamic
/// configuration.
/// </summary>
public class TenantDomainPartyConfiguration : ApplicationIdentityPartyConfiguration
{
    readonly ApplicationIdentityLocalConfiguration _localConfiguration;

    readonly List<RemotePartyConfiguration> _remotes;
    readonly List<RemotePartyTemplate> _remoteTemplates;
    TenantDomainTemplate? _template;
    ConfigurationInstance? _instance;

    internal TenantDomainPartyConfiguration( ImmutableConfigurationSection configuration,
                                             string domainName,
                                             NormalizedPath fullName,
                                             ApplicationIdentityLocalConfiguration local,
                                             List<RemotePartyConfiguration> remotes,
                                             List<RemotePartyTemplate> remoteTemplates,
                                             ref ResolvedConfigurationProps props )
        : base( configuration, domainName, fullName, ref props )
    {
        Throw.DebugAssert( CoreApplicationIdentity.TryParseFullName( fullName.Path, out var d, out var p, out var e )
                      && d == domainName && p == fullName.Parts[^2] && e == fullName.LastPart
                      && p[0] == '$' && p.AsSpan( 1 ).Equals( fullName.Parts[^3], StringComparison.OrdinalIgnoreCase ) );
        _localConfiguration = local;
        _remotes = remotes;
        _remoteTemplates = remoteTemplates;
    }

    /// <summary>
    /// Gets the remotes configurations.
    /// This can be empty.
    /// </summary>
    public IReadOnlyCollection<RemotePartyConfiguration> Remotes => _remotes;

    /// <summary>
    /// Gets the Opened remote templates: their dynamic instances are remotes of this tenant domain.
    /// </summary>
    public IReadOnlyList<RemotePartyTemplate> RemoteTemplates => _remoteTemplates;

    /// <summary>
    /// Gets the "Local" condfiguration.
    /// </summary>
    public ApplicationIdentityLocalConfiguration LocalConfiguration => _localConfiguration;

    /// <summary>
    /// Gets the Opened template of this tenant domain when it is one of its instances, null otherwise (a Closed template
    /// is expanded: its instances are configured tenant domains).
    /// </summary>
    public TenantDomainTemplate? Template => _template;

    /// <summary>
    /// Gets the instance of the <see cref="Template"/> that this tenant domain is, null when it is not the instance of an
    /// Opened template.
    /// </summary>
    public ConfigurationInstance? Instance => _instance;

    /// <summary>
    /// Gets whether this tenant domain is a run-time instance of an Opened template: it is not configured, it has been
    /// created at run time (its index is kept in its store: see <see cref="TenantDomainTemplate"/>).
    /// </summary>
    public bool IsDynamicInstance => _instance != null && _template!.Instances.IsDynamicIndex( _instance.Index );

    internal void SetInstance( TenantDomainTemplate template, ConfigurationInstance instance )
    {
        _template = template;
        _instance = instance;
    }
}
