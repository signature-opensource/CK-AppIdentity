using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.AccessControl;

namespace CK.AppIdentity
{
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

        internal readonly record struct ProcessedConfiguration(List<TenantDomainPartyConfiguration> Tenants, List<RemotePartyConfiguration> Remotes)
        {
            public int Count => Tenants.Count + Remotes.Count;
            public IEnumerable<ApplicationIdentityPartyConfiguration> Parties => ((IEnumerable<ApplicationIdentityPartyConfiguration>)Remotes).Concat(Tenants);
        }

        /// <summary>
        /// Tries to create one or more party configurations from a configuration with a "Dynamic" key that
        /// inherits from this configuration.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="configuration">The dynamic configurator.</param>
        /// <returns>One or more party configuration or null if an error occurred.</returns>
        internal ProcessedConfiguration? CreateDynamicRemoteConfiguration( IActivityMonitor monitor,
                                                                           Action<MutableConfigurationSection> configuration )
        {
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
            if( ApplicationIdentityServiceConfiguration.ReadParties( monitor,
                                                                     finalConfig,
                                                                     _domainName,
                                                                     _environmentName,
                                                                     ref inheritedProps,
                                                                     ref partyCollector,
                                                                     fullNameIndex ) )
            {
                return partyCollector;
            }
            return null;
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
                Throw.DebugAssert( string.IsInterned( "<error>" ) != null && string.IsInterned( "External" ) != null );
                domainName = NormalizeDomainName( monitor, d );
                success &= !ReferenceEquals( domainName, "<error>" );
                partyName = p;
                environmentName = e;
                return success;
            }

            static bool ReadName( IActivityMonitor monitor, ImmutableConfigurationSection s, NameKind kind, out string name, string? defaultName )
            {
                var k = _names[(int)kind];
                var n = s[k];
                if( n != null )
                {
                    bool isValid = kind switch
                    {
                        NameKind.Domain => CoreApplicationIdentity.IsValidDomainName( n ),
                        NameKind.Party => CoreApplicationIdentity.IsValidPartyName( n ),
                        NameKind.Env => CoreApplicationIdentity.IsValidEnvironmentName( n ),
                        _ => Throw.NotSupportedException<bool>()
                    };
                    if( !isValid )
                    {
                        monitor.Error( $"Invalid '{s.Path}:{k}'. It {_nameSyntaxes[(int)NameKind.Env]}" );
                        return ErrorName( kind, out name );
                    }
                    if( kind == NameKind.Domain )
                    {
                        name = NormalizeDomainName( monitor, n );
                        return name != "error";
                    }
                    name = n;
                }
                else
                {
                    if( defaultName == null )
                    {
                        monitor.Error( $"Configuration '{s.Path}:{k}' is required." );
                        return ErrorName( kind, out name );
                    }
                    name = defaultName;
                }
                return true;

                static bool ErrorName( NameKind kind, out string name )
                {
                    name = kind switch { NameKind.Party => "$error", NameKind.Env => "#error", _ => "error" };
                    return false;
                }
            }

            static string NormalizeDomainName( IActivityMonitor monitor, string domainName )
            {
                if( domainName.StartsWith( "External", StringComparison.OrdinalIgnoreCase ) )
                {
                    return CheckNoSubDomain( monitor, domainName, "External" );
                }
                if( domainName.StartsWith( CoreApplicationIdentity.DefaultDomainName, StringComparison.OrdinalIgnoreCase ) )
                {
                    return CheckNoSubDomain( monitor, domainName, CoreApplicationIdentity.DefaultDomainName );
                }
                return domainName;

                static string CheckNoSubDomain( IActivityMonitor monitor, string domainName, string prefix )
                {
                    int prefixLen = prefix.Length;
                    if( domainName.Length > prefixLen && domainName[prefixLen] == '/' )
                    {
                        monitor.Error( $"Domain name cannot start with \"{prefix}\". This denotes an \"External\" system where domains don't apply." );
                        return "error";
                    }
                    return "External";
                }
            }
        }

        #endregion
    }
}
