using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace CK.AppIdentity
{
    /// <summary>
    /// Configuration that defines the initial identity of an application.
    /// This is designed to be available as a singleton service in the DI container (the package CK.AppIdentity.Configuration does that).
    /// <para>
    /// Configurations are immutable (any existing configuration cannot be changed) but dynamic remote or tenant domain parties can be added
    /// and destroyed.
    /// </para>
    /// </summary>
    [SingletonService]
    public sealed class ApplicationIdentityServiceConfiguration : ApplicationIdentityPartyConfiguration
    {
        readonly List<RemotePartyConfiguration> _remotes;
        readonly List<TenantDomainPartyConfiguration> _tenants;
        readonly NormalizedPath _storeRootPath;
        readonly ApplicationIdentityLocalConfiguration _localConfiguration;
        readonly bool _strictMode;
        static NormalizedPath _defaultStoreRootPath;
        static readonly object _defaultStoreRootPathLock = new object();

        ApplicationIdentityServiceConfiguration( ImmutableConfigurationSection configuration,
                                                 string domainName,
                                                 NormalizedPath fullName,
                                                 ApplicationIdentityLocalConfiguration localConfiguration,
                                                 bool strictMode,
                                                 string store,
                                                 ref ProcessedConfiguration? parties,
                                                 ref InheritedConfigurationProps inhProps )
            : base( configuration, domainName, fullName, ref inhProps )
        {
            Throw.DebugAssert( parties.HasValue );
            _storeRootPath = store;
            _remotes = parties.Value.Remotes;
            _tenants = parties.Value.Tenants;
            _localConfiguration = localConfiguration;
            _strictMode = strictMode;
        }

        // Constructor for the empty.
        ApplicationIdentityServiceConfiguration( ImmutableConfigurationSection configuration,
                                                 string domainName,
                                                 NormalizedPath fullName,
                                                 NormalizedPath? storeRootPath,
                                                 ref InheritedConfigurationProps inhProps )
            : base( configuration, domainName, fullName, ref inhProps )
        {
            _storeRootPath = storeRootPath ?? DefaultStoreRootPath;
            _remotes = new List<RemotePartyConfiguration>();
            _tenants = new List<TenantDomainPartyConfiguration>();
            _strictMode = EnvironmentName != CoreApplicationIdentity.DefaultEnvironmentName;
            var local = configuration.GetSection( "Local" );
            _localConfiguration = new ApplicationIdentityLocalConfiguration( local, ref inhProps );
        }

        /// <summary>
        /// Gets the remote parties if any.
        /// </summary>
        public IReadOnlyCollection<RemotePartyConfiguration> Remotes => _remotes;

        /// <summary>
        /// Gets the tenant domains if any.
        /// </summary>
        public IReadOnlyCollection<TenantDomainPartyConfiguration> TenantDomains => _tenants;

        /// <summary>
        /// Gets whether this configuration must be strictly checked: warnings are considered errors.
        /// <para>
        /// This always defaults to true except when this EnvironmentName is "#Dev": we consider that
        /// by default, in development, a configuration can have warnings but in any other environment
        /// (typically in "#Production"), a configuration must be perfectly valid.
        /// </para>
        /// </summary>
        public bool StrictConfigurationMode => _strictMode;

        /// <summary>
        /// Gets the "Local" configuration section.
        /// </summary>
        public ApplicationIdentityLocalConfiguration LocalConfiguration => _localConfiguration;

        /// <summary>
        /// Gets the file storage root path. Defaults to <see cref="DefaultStoreRootPath"/>.
        /// <para>
        /// This folder is de facto shared by all applications (parties) that use CK.AppIdentity and run on this computer.
        /// Such installed parties can use <see cref="ILocalParty.LocalFileStore"/> to store any application 
        /// specific data. All installed parties can use <see cref="IParty.SharedFileStore"/> to store and share data related to parties.
        /// </para>
        /// </summary>
        public NormalizedPath StoreRootPath => _storeRootPath;

        /// <summary>
        /// Gets or sets the default store path that is by default "<see cref="Environment.SpecialFolder.LocalApplicationData"/>/CK-AppIdentity".
        /// <para>
        /// This is primarily intended for tests and must be set prior to any access to this property: once this property is accessed or set,
        /// its value is settled. 
        /// </para>
        /// </summary>
        public static NormalizedPath DefaultStoreRootPath
        {
            get
            {
                var p = _defaultStoreRootPath;
                if( p.IsEmptyPath )
                {
                    lock( _defaultStoreRootPathLock )
                    {
                        p = _defaultStoreRootPath;
                        if( p.IsEmptyPath )
                        {
                            p = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify );
                            p = Path.Combine( p, "CK-AppIdentity" );
                        }
                    }
                    _defaultStoreRootPath = p;
                }
                return p;
            }
            set
            {
                var p = _defaultStoreRootPath;
                if( p.IsEmptyPath )
                {
                    lock( _defaultStoreRootPathLock )
                    {
                        p = _defaultStoreRootPath;
                        if( p.IsEmptyPath ) p = value;
                    }
                    _defaultStoreRootPath = p;
                }
            }
        }

        /// <summary>
        /// Creates an empty configuration.
        /// <para>
        /// Using this configuration for a <see cref="ApplicationIdentityService"/> allows dynamic remote parties
        /// to be added (and destroyed) but prevents the <see cref="LocalParty"/> to be altered.
        /// </para>
        /// </summary>
        /// <param name="domainName">Domain name.</param>
        /// <param name="partyName">Party name.</param>
        /// <param name="environmentName">Environment name.</param>
        /// <param name="storeRootPath">Optional store root path. Defaults to <see cref="DefaultStoreRootPath"/>.</param>
        /// <returns>An empty configuration.</returns>
        public static ApplicationIdentityServiceConfiguration CreateEmpty( string domainName = CoreApplicationIdentity.DefaultDomainName,
                                                                           string partyName = CoreApplicationIdentity.DefaultPartyName,
                                                                           string environmentName = CoreApplicationIdentity.DefaultEnvironmentName,
                                                                           NormalizedPath? storeRootPath = null )
        {
            CoreApplicationIdentity.IsValidDomainName( domainName );
            CoreApplicationIdentity.IsValidPartyName( partyName );
            CoreApplicationIdentity.IsValidPartyName( environmentName );
            if( partyName[0] != '$' ) partyName = '$' + partyName;
            var props = new InheritedConfigurationProps( ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty, AssemblyConfiguration.Empty );
            var c = new MutableConfigurationSection( "CK-AppIdentity" );
            return new ApplicationIdentityServiceConfiguration( new ImmutableConfigurationSection( c ),
                                                                domainName,
                                                                $"{domainName}/{partyName}/{environmentName}",
                                                                storeRootPath ?? DefaultStoreRootPath,
                                                                ref props );
        }

        /// <summary>
        /// Tries to create an <see cref="ApplicationIdentityServiceConfiguration"/> instance from a <see cref="IConfigurationSection"/>
        /// and the <see cref="IHostEnvironment"/>: the <see cref="IHostEnvironment.ApplicationName"/> is the default party name
        /// and <see cref="IHostEnvironment.EnvironmentName"/> is the default environment name.
        /// <para>
        /// If the configuration doesn't specify the "DomainName" (or doesn't define the "FullName" of the party), "Default" domain name
        /// is used.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="hostEnvironment">The hosting environment from which defaults party and environment names are used.</param>
        /// <param name="configuration">The configuration section (typically named "CK-AppIdentity").</param>
        /// <returns>A valid instance on success, null on configuration error.</returns>
        public static ApplicationIdentityServiceConfiguration? Create( IActivityMonitor monitor,
                                                                       IHostEnvironment hostEnvironment,
                                                                       IConfigurationSection configuration )
        {
            var env = hostEnvironment.EnvironmentName;
            if( string.IsNullOrWhiteSpace( env ) || env == "Development" )
            {
                env = CoreApplicationIdentity.DefaultEnvironmentName;
            }
            else if( env[0] != '#' )
            {
                env = '#' + env;
            }
            if( env.Length > CoreApplicationIdentity.EnvironmentNameMaxLength )
            {
                env = env.Substring( 0, CoreApplicationIdentity.EnvironmentNameMaxLength );
            }
            return Create( monitor, configuration, "Default", hostEnvironment.ApplicationName, env );
        }

        /// <summary>
        /// Tries to create an <see cref="ApplicationIdentityServiceConfiguration"/> instance from a "CK-AppIdentity" <see cref="MutableConfigurationSection"/>
        /// that is setup by a callback. At least "DomainName" and "PartyName" (or "FullName") configuration must be set ("EnvironmentName" defaults
        /// to "#Dev").
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="configuration">Must configure the "CK-AppIdentity" section.</param>
        /// <returns>A valid instance on success, null on configuration error.</returns>
        public static ApplicationIdentityServiceConfiguration? Create( IActivityMonitor monitor,
                                                                       Action<MutableConfigurationSection> configuration )
        {
            var c = new MutableConfigurationSection( "CK-AppIdentity" );
            configuration( c );
            return Create( monitor, c );
        }

        sealed class WarnTracker : IActivityMonitorClient, IDisposable
        {
            private readonly IActivityMonitorOutput _output;
            int _warnCount;

            public WarnTracker( IActivityMonitorOutput output )
            {
                _output = output;
                output.RegisterClient( this );
            }

            public int WarnCount => _warnCount;

            public void Dispose()
            {
                _output.UnregisterClient( this );
            }

            public void OnUnfilteredLog( ref ActivityMonitorLogData data )
            {
                if( data.MaskedLevel == LogLevel.Warn ) _warnCount++;
            }

            public void OnOpenGroup( IActivityLogGroup group )
            {
                if( group.Data.MaskedLevel == LogLevel.Warn ) _warnCount++;
            }

            public void OnGroupClosing( IActivityLogGroup group, ref List<ActivityLogGroupConclusion>? conclusions )
            {
            }

            public void OnGroupClosed( IActivityLogGroup group, IReadOnlyList<ActivityLogGroupConclusion> conclusions )
            {
            }

            public void OnTopicChanged( string newTopic, string? fileName, int lineNumber )
            {
            }

            public void OnAutoTagsChanged( CKTrait newTrait )
            {
            }
        }

        /// <summary>
        /// Tries to create an <see cref="ApplicationIdentityServiceConfiguration"/> instance from a <see cref="IConfigurationSection"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="configuration">The configuration section (typically named "CK-AppIdentity").</param>
        /// <param name="defaultDomainName">A valid domain name to use if the <paramref name="configuration"/> doesn't specify "DomainName".</param>
        /// <param name="defaultPartyName">A valid party name to use if the <paramref name="configuration"/> doesn't specify "PartyName".</param>
        /// <param name="defaultEnvironmentName">A valid environment name to use if the <paramref name="configuration"/> doesn't specify "EnvironmentName".</param>
        /// <returns>A valid instance on success, null on configuration error.</returns>
        public static ApplicationIdentityServiceConfiguration? Create( IActivityMonitor monitor,
                                                                       IConfigurationSection configuration,
                                                                       string? defaultDomainName = null,
                                                                       string? defaultPartyName = null,
                                                                       string defaultEnvironmentName = CoreApplicationIdentity.DefaultEnvironmentName )
        {
            Throw.CheckNotNullArgument( defaultEnvironmentName );
            using var gLog = monitor.OpenInfo( "Creating root ApplicationIdentityServiceConfiguration service." );
            var root = configuration as ImmutableConfigurationSection ?? new ImmutableConfigurationSection( configuration );

            // ReadNames is strict.
            bool success = ReadNames( monitor, root,
                                      out var domainName, out var partyName, out var environmentName,
                                      defaultDomainName, defaultPartyName, defaultEnvironmentName )
                           & InheritedConfigurationProps.TryCreate( monitor, root, out var props );

            Throw.DebugAssert( nameof( StrictConfigurationMode ) == "StrictConfigurationMode" );
            bool strictMode =  environmentName != CoreApplicationIdentity.DefaultEnvironmentName;
            var sStrict = root.TryLookupSection( "StrictConfigurationMode" );
            if( sStrict != null && !bool.TryParse( sStrict.Value, out strictMode ) )
            {
                monitor.Error( $"StrictConfigurationMode, when defined, must be a 'true' or 'false' boolean." );
                success = false;
            }

            if( ReferenceEquals( domainName, "External" ) )
            {
                Throw.DebugAssert( CoreApplicationIdentity.DefaultDomainName == "Undefined" );
                monitor.Error( $"Root domain name cannot be \"External\" or \"Undefined\". This name denotes an external system." );
                success = false;
            }

            monitor.MinimalFilter = monitor.MinimalFilter.Combine( LogFilter.Minimal );

            var warnAsError = strictMode ? new WarnTracker( monitor.Output ) : null;

            // Always try to create the parties even if success is already false: this enables
            // configuration errors to be fixed at once.
            var fullNameIndex = new Dictionary<string, ImmutableConfigurationSection>( StringComparer.OrdinalIgnoreCase );
            var parties = CreateParties( monitor, root.GetSection( "Parties" ), domainName, environmentName, ref props, fullNameIndex );
            success &= parties.HasValue;

            var store = HandleStorePath( monitor, configuration );
            success &= store != null;

            if( warnAsError != null )
            {
                warnAsError.Dispose();
                if( warnAsError.WarnCount > 0 )
                {
                    monitor.Error( $"{warnAsError.WarnCount} warnings occurred and StrictConfigurationMode is true: no warning must be emitted." );
                    success = false;
                }
            }
            // Success may become false if something fails in the local configuration.
            var localConfig = CreateLocalConfiguration( monitor, root, ref props, ref success );

            if( !success )
            {
                monitor.CloseGroup( "Failed." );
                return null;
            }
            var fullName = partyName[0] == '$' ? $"{domainName}/{partyName}/{environmentName}" : $"{domainName}/${partyName}/{environmentName}";
            return new ApplicationIdentityServiceConfiguration( root, domainName, fullName, localConfig, strictMode, store!, ref parties, ref props );

            static string? HandleStorePath( IActivityMonitor monitor, IConfigurationSection configuration )
            {
                var store = configuration[nameof( StoreRootPath )]?.Trim();
                if( !string.IsNullOrEmpty( store ) )
                {
                    if( FileUtil.IndexOfInvalidPathChars( store ) >= 0 )
                    {
                        monitor.Error( $"Invalid path '{configuration.Path}:{nameof( StoreRootPath )}'. Invalid characters in '{store}'." );
                        return null;
                    }
                    if( !Path.IsPathFullyQualified( store ) )
                    {
                        monitor.Error( $"Invalid path '{configuration.Path}:{nameof( StoreRootPath )}'. '{store}' must not be relative." );
                        return null;
                    }
                }
                else
                {
                    store = DefaultStoreRootPath;
                }
                try
                {
                    Directory.CreateDirectory( store );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"Unable to create store directory '{store}'.", ex );
                    return null;
                }
                return store;
            }
        }

        static ProcessedConfiguration? CreateParties( IActivityMonitor monitor,
                                                      ImmutableConfigurationSection configuration,
                                                      string domainName,
                                                      string environmentName,
                                                      ref InheritedConfigurationProps props,
                                                      Dictionary<string, ImmutableConfigurationSection> fullNameIndex )
        {
            Throw.DebugAssert( configuration.Key == "Parties" );
            bool success = props.IsValid;
            var parties = new ProcessedConfiguration( new List<TenantDomainPartyConfiguration>(), new List<RemotePartyConfiguration>() );
            foreach( var c in configuration.GetChildren() )
            {
                success &= ReadParties( monitor, c, domainName, environmentName, ref props, ref parties, fullNameIndex );
            }
            return success ? parties : null;
        }

        internal static bool ReadParties( IActivityMonitor monitor,
                                          ImmutableConfigurationSection configuration,
                                          string inhDomainName,
                                          string inhEnvironmentName,
                                          ref InheritedConfigurationProps inhProps,
                                          ref ProcessedConfiguration partyCollector,
                                          Dictionary<string, ImmutableConfigurationSection> fullNameIndex )
        {
            var partiesSection = configuration.GetSection( "Parties" );
            bool nameSuccess = ReadNames(monitor, configuration,
                                          out var domainName, out var partyName, out var environmentName,
                                          inhDomainName, "", inhEnvironmentName );
            // Always try to propagate the inherited props.
            bool success = nameSuccess & InheritedConfigurationProps.TryCreate( monitor, inhProps, configuration, out var props );
            // If a PartyName is not defined, it is a pure group. We handle it recursively, handling inherited names and properties.
            if( partyName.Length == 0 )
            {
                using var gLog = monitor.OpenInfo( $"Party group found '{partiesSection.Path}'." );
                int count = partyCollector.Count;
                foreach( var c in configuration.GetChildren() )
                {
                    success &= ReadParties( monitor, c, domainName, environmentName, ref props, ref partyCollector, fullNameIndex );
                }
                if( success ) monitor.CloseGroup( $"Found {partyCollector.Count - count} parties." );
                else monitor.CloseGroup( "Failed." );
                return success;
            }
            // It is a Party: a TenantDomainPartyConfiguration or a RemotePartyConfiguration.
            bool isDomain;
            NormalizedPath fullName;
            string? address = configuration["Address"];
            if( partyName[0] == '$' )
            {
                fullName = $"{domainName}/{partyName}/{environmentName}";
                isDomain = address == null && partyName.AsSpan( 1 ).Equals( fullName.Parts[^3], StringComparison.OrdinalIgnoreCase );
            }
            else
            {
                fullName = $"{domainName}/${partyName}/{environmentName}";
                isDomain = address == null && partyName.Equals( fullName.Parts[^3], StringComparison.OrdinalIgnoreCase );
            }
            // Check full name unicity in this whole configuration only if the names have been successfully read.
            Throw.DebugAssert( fullNameIndex.Comparer == StringComparer.OrdinalIgnoreCase );
            if( nameSuccess )
            {
                if( fullNameIndex.TryGetValue( fullName, out var exists ) )
                {
                    monitor.Error( $"Duplicate party definition '{configuration.Path}': '{fullName}' is already defined by '{exists.Path}'." );
                    success = false;
                }
                else
                {
                    fullNameIndex.Add( fullName, configuration );
                }
            }
            if( isDomain )
            {
                using var gLog = monitor.OpenInfo( $"Found domain definition '{fullName}'." );
                var parties = CreateParties( monitor, partiesSection, domainName, environmentName, ref props, fullNameIndex );
                if( parties.HasValue )
                {
                    // Lifts the tenants found below.
                    partyCollector.Tenants.AddRange( parties.Value.Tenants );
                    // Adds the found tenant if there's no issue with its names.
                    if( nameSuccess )
                    {
                        // Success may become false if something fails in the local configuration.
                        var localConfig = CreateLocalConfiguration( monitor, configuration, ref props, ref success );
                        partyCollector.Tenants.Add( new TenantDomainPartyConfiguration( configuration, domainName, fullName, localConfig, parties.Value.Remotes, ref props ) );
                    }
                }
                else
                {
                    success = false;
                }
            }
            else
            {
                if( success )
                {
                    var p = new RemotePartyConfiguration( configuration, domainName, fullName, address, ref props );
                    partyCollector.Remotes.Add( p );
                    monitor.Info( $"Found '{fullName}' {(p.IsExternalParty ? "external " : "")}remote party." );
                }
            }
            return success;
        }

        static ApplicationIdentityLocalConfiguration CreateLocalConfiguration( IActivityMonitor monitor,
                                                                               ImmutableConfigurationSection configuration,
                                                                               ref InheritedConfigurationProps props,
                                                                               ref bool success )
        {
            // This creates an empty section if "Local" is not defined: this is exactly what we want.
            var local = configuration.GetSection( "Local" );
            success &= InheritedConfigurationProps.TryCreate( monitor, props, configuration, out var localProps );
            var localConfig = new ApplicationIdentityLocalConfiguration( local, ref localProps );
            return localConfig;
        }
    }
}
