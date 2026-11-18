using CK.Core;
using System.Diagnostics;

namespace CK.AppIdentity;

/// <summary>
/// Remote or External party configuration.
/// The Address is always optional.
/// <para>
/// An external party is in the domain "External", it may have an Address or not and its
/// <see cref="ApplicationIdentityPartyConfiguration.EnvironmentName"/> is mostly irrelevant.
/// </para>
/// </summary>
public sealed class RemotePartyConfiguration : ApplicationIdentityPartyConfiguration
{
    readonly string? _address;
    RemotePartyTemplate? _template;
    ConfigurationInstance? _instance;

    internal RemotePartyConfiguration( ImmutableConfigurationSection configuration,
                                       string domainName,
                                       NormalizedPath fullName,
                                       string? address,
                                       ref ResolvedConfigurationProps props )
        : base( configuration, domainName, fullName, ref props )
    {
        Throw.DebugAssert( CoreApplicationIdentity.TryParseFullName( fullName.Path, out var d, out var p, out var e )
                      && d == domainName && e == fullName.LastPart && p == fullName.Parts[^2] );

        _address = address;
    }

    /// <summary>
    /// Gets whether this is an External party: its <see cref="ApplicationIdentityPartyConfiguration.DomainName"/>
    /// is <see cref="ApplicationIdentityPartyConfiguration.ExternalDomainName"/>.
    /// </summary>
    public bool IsExternalParty => DomainName == ExternalDomainName;

    /// <summary>
    /// Gets the address of this party.
    /// This is null if this application cannot reach the remote: this remote must be a server that accepts the remote as a client).
    /// </summary>
    public string? Address => _address;

    /// <summary>
    /// Gets the template of this remote when it is the instance of a template, null otherwise.
    /// </summary>
    public RemotePartyTemplate? Template => _template;

    /// <summary>
    /// Gets the instance of the <see cref="Template"/> that this remote is, null when it is not the instance of a template.
    /// </summary>
    public ConfigurationInstance? Instance => _instance;

    /// <summary>
    /// Gets whether this remote is a dynamic instance of an Opened template: it is not configured, it exists because it
    /// has been approved (its identity is pinned in the store) or because it is a candidate to the approval.
    /// </summary>
    public bool IsDynamicInstance => _instance != null && _template!.IsDynamic( _instance );

    internal void SetInstance( RemotePartyTemplate template, ConfigurationInstance instance )
    {
        _template = template;
        _instance = instance;
    }

}
