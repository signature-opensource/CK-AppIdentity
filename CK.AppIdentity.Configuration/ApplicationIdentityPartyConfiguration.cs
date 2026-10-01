using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.AccessControl;

namespace CK.AppIdentity;

/// <summary>
/// Base class of all configuration objects (except <see cref="ApplicationIdentityLocalConfiguration"/>).
/// It can be the root application, a remote (an external remote when "DomainName" is "External")
/// or a tenant domain that contains its parties).
/// </summary>
public class ApplicationIdentityPartyConfiguration : ApplicationIdentityConfiguration
{
    readonly string _domainName;
    readonly string _partyName;
    readonly string _environmentName;
    readonly NormalizedPath _fullName;

    internal ApplicationIdentityPartyConfiguration( ImmutableConfigurationSection configuration,
                                                    string domainName,
                                                    NormalizedPath fullName,
                                                    ref InheritedConfigurationProps props )
        : base( configuration, ref props )
    {
        Throw.DebugAssert( CoreApplicationIdentity.TryParseFullName( fullName.Path, out var d, out var p, out var e )
                      && d == domainName && p == fullName.Parts[^2] && e == fullName.LastPart,
                      $"{fullName.Path} => d:{domainName}, p:{p}, e:{e}" );

        _domainName = domainName;
        _partyName = fullName.Parts[^2];
        _environmentName = fullName.LastPart;
        _fullName = fullName;
    }

    /// <summary>
    /// Gets the domain name.
    /// </summary>
    public string DomainName => _domainName;

    /// <summary>
    /// Gets the name of this party.
    /// </summary>
    public string PartyName => _partyName;

    /// <summary>
    /// Gets the environment name.
    /// </summary>
    public string EnvironmentName => _environmentName;

    /// <summary>
    /// Gets the full name of this party.
    /// </summary>
    public NormalizedPath FullName => _fullName;

    /// <summary>
    /// Defines the result of the <see cref="CreateDynamicRemoteConfiguration(IActivityMonitor, Action{MutableConfigurationSection}, bool)"/> method.
    /// </summary>
    /// <param name="Tenants">The list of tenant configurations.</param>
    /// <param name="Remotes">The list of remote configurations.</param>
    public readonly record struct ProcessedConfiguration( List<TenantDomainPartyConfiguration> Tenants, List<RemotePartyConfiguration> Remotes )
    {
        /// <summary>
        /// The total count of parties (remotes and tenants) in the configuration.
        /// </summary>
        public int Count => Tenants.Count + Remotes.Count;

        /// <summary>
        /// Gets all the parties (remotes and tenants) in the configuration.
        /// </summary>
        public IEnumerable<ApplicationIdentityPartyConfiguration> Parties => ((IEnumerable<ApplicationIdentityPartyConfiguration>)Remotes).Concat( Tenants );
    }

    /// <summary>
    /// Tries to create one or more party configurations from a configuration with a "Dynamic" key that
    /// inherits from this configuration.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The dynamic configurator.</param>
    /// <param name="strictConfigurationMode">
    /// When true, any warning is an error (this should be the <see cref="ApplicationIdentityServiceConfiguration.StrictConfigurationMode"/>
    /// of the root configuration).
    /// </param>
    /// <returns>One or more party configuration or null if an error occurred.</returns>
    public ProcessedConfiguration? CreateDynamicRemoteConfiguration( IActivityMonitor monitor,
                                                                     Action<MutableConfigurationSection> configuration,
                                                                     bool strictConfigurationMode = false )
    {
        using var warnTracker = new WarnTracker( monitor );
        // Anchors the new mutable section below this section: lookups apply.
        //
        // The "Remotes:X" levels are useless. We don't need these because these slots don't carry any
        // information other than the "collection" (array) and the "index" that we totally ignore.
        //
        var anchor = Configuration;
        var remotes = new MutableConfigurationSection( anchor );
        var c = remotes.GetMutableSection( "Dynamic" );
        Throw.DebugAssert( string.IsInterned( c.Key ) == "Dynamic" );
        configuration( c );
        var finalConfig = new ImmutableConfigurationSection( c, anchor );
        var inheritedProps = new InheritedConfigurationProps( this );
        // We obviously have a race condition here on the full name unicity.
        // The fact that no full name conflict offers no guaranty when the new configuration
        // will be added.
        // The fact that a full name conflicts is more interesting... But without more
        // concurrency guaranty.
        // We don't inject any "existing" names here: it is up to the actual add to handle
        // existing remotes.
        var fullNameIndex = new Dictionary<string, ImmutableConfigurationSection>( StringComparer.OrdinalIgnoreCase );
        var partyCollector = new ProcessedConfiguration( new List<TenantDomainPartyConfiguration>(), new List<RemotePartyConfiguration>() );
        bool success = ApplicationIdentityServiceConfiguration.ReadParties( monitor,
                                                                            finalConfig,
                                                                            _domainName,
                                                                            _environmentName,
                                                                            ref inheritedProps,
                                                                            ref partyCollector,
                                                                            fullNameIndex );
        success &= warnTracker.Close( monitor, strictConfigurationMode );
        return success ? partyCollector : null;
    }

    /// <summary>
    /// Overridden to return this <see cref="FullName"/>.
    /// </summary>
    /// <returns>This full name.</returns>
    public override string ToString() => _fullName;

    #region ReadNames
    enum NameKind
    {
        Domain,
        Party,
        Env
    }

    static readonly string[] _names = new[] { "DomainName", "PartyName", "EnvironmentName" };

    static readonly string[] _nameSyntaxes = new[]
    {
        $"must be a case sensitive identifier or path of identifiers not longer than {CoreApplicationIdentity.DomainNameMaxLength}, "
        + $"no leading or trailing '/' and no double '//' are allowed. Identifier should use PascalCase convention if possible and must "
        + $"only contain 'A'-'Z', 'a'-'z', '0'-'9', '-' and '_' characters and must not start with a digit, and not start or end with '_' or '-'.",

        $"should use PascalCase convention if possible and must only contain 'A'-'Z', 'a'-'z', '0'-'9', '-' and '_' characters and "
        + $"must not start with a digit, and not start or end with '_' or '-' or be longer than {CoreApplicationIdentity.PartyNameMaxLength}.",

        $"must start with a '#', should use PascalCase convention if possible and must only contain 'A'-'Z', 'a'-'z', '0'-'9', '-' and '_'  "
        + $"or be longer than {CoreApplicationIdentity.EnvironmentNameMaxLength}."
    };

    private protected static bool ReadNames( IActivityMonitor monitor,
                                             ImmutableConfigurationSection s,
                                             out string domainName,
                                             out string partyName,
                                             out string environmentName,
                                             string? defaultDomainName = null,
                                             string? defaultPartyName = null,
                                             string? defaultEnvironmentName = null )
    {
        var f = s["FullName"];
        if( f != null )
        {
            return ReadFromFullName( monitor, s, f, out domainName, out partyName, out environmentName, defaultPartyName, defaultEnvironmentName );
        }
        // No shortcut operators here to collect all the errors.
        return ReadName( monitor, s, NameKind.Domain, out domainName, defaultDomainName )
               & ReadName( monitor, s, NameKind.Party, out partyName, defaultPartyName )
               & ReadName( monitor, s, NameKind.Env, out environmentName, defaultEnvironmentName );

        static bool ReadFromFullName( IActivityMonitor monitor,
                                      ImmutableConfigurationSection s,
                                      string fullName,
                                      out string domainName,
                                      out string partyName,
                                      out string environmentName,
                                      string? defaultPartyName,
                                      string? defaultEnvironmentName )
        {
            if( !CoreApplicationIdentity.TryParseFullName( fullName, out var d, out var p, out var e ) )
            {
                monitor.Error( $"Invalid '{s.Path}:FullName'. '{fullName}' is not a valid party full name." );
                domainName = partyName = environmentName = "<error>";
                return false;
            }
            bool success = true;
            if( p == null )
            {
                success &= ReadName( monitor, s, NameKind.Party, out p, defaultPartyName );
            }
            else if( s["PartyName"] != null )
            {
                monitor.Error( $"'{s.Path}:PartyName' cannot be used when '{s.Path}:FullName' defines it." );
                success = false;
            }

            if( e == null )
            {
                success &= ReadName( monitor, s, NameKind.Env, out e, defaultEnvironmentName );
            }
            else if( s["EnvironmentName"] != null )
            {
                monitor.Error( $"'{s.Path}:EnvironmentName' cannot be used when '{s.Path}:FullName' defines it." );
                success = false;
            }

            if( s["DomainName"] != null )
            {
                monitor.Error( $"'{s.Path}:DomainName' cannot be used when '{s.Path}:FullName' is defined." );
                success = false;
            }
            success &= NormalizeDomainName( monitor, d, out domainName );
            partyName = p;
            environmentName = NormalizeEnvironmentName( e );
            return success;
        }

        static bool ReadName( IActivityMonitor monitor, ImmutableConfigurationSection s, NameKind kind, out string name, string? defaultName )
        {
            var k = _names[(int)kind];
            var n = s[k];
            if( n == null )
            {
                if( defaultName == null )
                {
                    monitor.Error( $"Configuration '{s.Path}:{k}' is required." );
                    return ErrorName( kind, out name );
                }
                // An empty default party name denotes a group of parties (see ReadParties).
                if( kind == NameKind.Party && defaultName.Length == 0 )
                {
                    name = defaultName;
                    return true;
                }
                // Defaults come from code or from the host (the IHostEnvironment.ApplicationName is
                // typically an assembly name like "Acme.Service"): they are checked like configured names.
                if( !IsValid( kind, defaultName ) )
                {
                    monitor.Error( $"Configuration '{s.Path}:{k}' must be defined: its default value '{defaultName}' is invalid. It {_nameSyntaxes[(int)kind]}" );
                    return ErrorName( kind, out name );
                }
                n = defaultName;
            }
            else if( !IsValid( kind, n ) )
            {
                monitor.Error( $"Invalid '{s.Path}:{k}'. It {_nameSyntaxes[(int)kind]}" );
                return ErrorName( kind, out name );
            }
            if( kind == NameKind.Domain )
            {
                return NormalizeDomainName( monitor, n, out name );
            }
            name = kind == NameKind.Env ? NormalizeEnvironmentName( n ) : n;
            return true;

            static bool IsValid( NameKind kind, string n ) => kind switch
            {
                NameKind.Domain => CoreApplicationIdentity.IsValidDomainName( n ),
                NameKind.Party => CoreApplicationIdentity.IsValidPartyName( n ),
                NameKind.Env => CoreApplicationIdentity.IsValidEnvironmentName( n ),
                _ => Throw.NotSupportedException<bool>()
            };

            static bool ErrorName( NameKind kind, out string name )
            {
                name = kind switch { NameKind.Party => "$error", NameKind.Env => "#error", _ => "error" };
                return false;
            }
        }

        // Same as the CoreApplicationIdentity.Builder.EnvironmentName.
        static string NormalizeEnvironmentName( string environmentName )
        {
            return environmentName.Equals( "#Development", StringComparison.OrdinalIgnoreCase )
                    ? CoreApplicationIdentity.DefaultEnvironmentName
                    : environmentName;
        }

        // "External" and "Undefined" (the CoreApplicationIdentity.DefaultDomainName) denote an external system:
        // they are normalized to "External" and can't have sub domains. Only the whole first segment is considered:
        // "ExternalPartners" is a regular domain.
        static bool NormalizeDomainName( IActivityMonitor monitor, string domainName, out string normalized )
        {
            var firstSegment = domainName.AsSpan( 0, domainName.IndexOf( '/' ) is var idx && idx >= 0 ? idx : domainName.Length );
            if( firstSegment.Equals( ExternalDomainName, StringComparison.OrdinalIgnoreCase )
                || firstSegment.Equals( CoreApplicationIdentity.DefaultDomainName, StringComparison.OrdinalIgnoreCase ) )
            {
                if( firstSegment.Length < domainName.Length )
                {
                    monitor.Error( $"Domain name '{domainName}' cannot start with \"{firstSegment}\". This denotes an \"External\" system where domains don't apply." );
                    normalized = "error";
                    return false;
                }
                normalized = ExternalDomainName;
                return true;
            }
            normalized = domainName;
            return true;
        }
    }

    /// <summary>
    /// The "External" domain name: the domain of the external parties. "Undefined" (the <see cref="CoreApplicationIdentity.DefaultDomainName"/>)
    /// is normalized to "External".
    /// </summary>
    public const string ExternalDomainName = "External";

    #endregion
}
