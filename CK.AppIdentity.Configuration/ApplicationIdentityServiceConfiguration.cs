using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;

namespace CK.AppIdentity;

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
    readonly StoreFileSystem _storeFileSystem;
    readonly ApplicationIdentityLocalConfiguration _localConfiguration;
    readonly bool _strictMode;
    TimeSpan _trashBinRetention = DefaultTrashBinRetention;
    // A string (not a NormalizedPath struct) so that it is read and written atomically.
    static string? _defaultStoreRootPath;
    static readonly object _defaultStoreRootPathLock = new object();

    /// <summary>
    /// The "Default" domain name used when no domain name is configured for the root party
    /// (the <see cref="CoreApplicationIdentity.DefaultDomainName"/> "Undefined" denotes an external party).
    /// </summary>
    public const string DefaultRootDomainName = "Default";

    ApplicationIdentityServiceConfiguration( ImmutableConfigurationSection configuration,
                                             string domainName,
                                             NormalizedPath fullName,
                                             ApplicationIdentityLocalConfiguration localConfiguration,
                                             bool strictMode,
                                             StoreFileSystem store,
                                             ref ProcessedConfiguration? parties,
                                             ref InheritedConfigurationProps inhProps )
        : base( configuration, domainName, fullName, ref inhProps )
    {
        Throw.DebugAssert( parties.HasValue );
        _storeFileSystem = store;
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
        _storeFileSystem = new StoreFileSystem( storeRootPath ?? DefaultStoreRootPath, isPrivate: !storeRootPath.HasValue );
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
    /// This folder is shared by all applications (parties) that use CK.AppIdentity, run on this computer
    /// and use the same root. Such installed parties can use <see cref="ILocalParty.LocalFileStore"/> to store
    /// any application specific data. All installed parties can use <see cref="IParty.SharedFileStore"/> to store
    /// and share data related to parties.
    /// </para>
    /// <para>
    /// The store holds trust anchors: anyone able to create, delete or rename files in it can repoint the trusted
    /// identity of a remote. When this is not configured, the store is private (see <see cref="IsPrivateStore"/>):
    /// it is restricted to the current account, which is anyway the only account that can share it since the default
    /// root is in the user's profile. When configured, the store is shared and its permissions are managed by the
    /// operator: on Unix, folders and files take the owner and group permissions of the root, never the "others" ones
    /// (use a setgid group root like 2770); on Windows, they inherit the root's ACL as-is. Applications sharing a store
    /// must run under accounts of this group.
    /// </para>
    /// <para>
    /// A store writable by every local account is reported by a warning (an error in <see cref="StrictConfigurationMode"/>).
    /// Note that writes in a shared store are not synchronized between processes.
    /// </para>
    /// </summary>
    public NormalizedPath StoreRootPath => _storeFileSystem.Root;

    /// <summary>
    /// Gets whether the store is private: <see cref="StoreRootPath"/> is not configured and the store is
    /// restricted to the current account. When false, the store is shared and its permissions are
    /// managed by the operator. See <see cref="StoreRootPath"/>.
    /// </summary>
    public bool IsPrivateStore => _storeFileSystem.IsPrivate;

    internal StoreFileSystem StoreFileSystem => _storeFileSystem;

    /// <summary>
    /// The default <see cref="TrashBinRetention"/> is 7 days.
    /// </summary>
    public static readonly TimeSpan DefaultTrashBinRetention = TimeSpan.FromDays( 7 );

    /// <summary>
    /// Gets how long trashed files (see <c>IFileStore.TryTrash</c>) are kept in the trash bins.
    /// Defaults to <see cref="DefaultTrashBinRetention"/> (7 days). It is configured by "TrashBinRetention"
    /// (a <see cref="TimeSpan"/> like "7.00:00:00" or "12:00:00"); 0 deletes the trashed files at the next purge.
    /// <para>
    /// The trash bins are purged when the service starts, every 6 hours, when it shuts down and when a party is destroyed.
    /// </para>
    /// <para>
    /// The trash bins may contain trashed trust anchors or keys: this should be kept short.
    /// </para>
    /// </summary>
    public TimeSpan TrashBinRetention => _trashBinRetention;

    /// <summary>
    /// Gets or sets the default store path that is by default "<see cref="Environment.SpecialFolder.LocalApplicationData"/>/CK-AppIdentity".
    /// <para>
    /// This is primarily intended for tests and must be set prior to any access to this property: once this property is accessed or set,
    /// its value is settled and setting a different value throws an <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// The value must be a valid fully qualified path (otherwise an <see cref="ArgumentException"/> is thrown).
    /// </para>
    /// </summary>
    public static NormalizedPath DefaultStoreRootPath
    {
        get
        {
            var p = Volatile.Read( ref _defaultStoreRootPath );
            if( p == null )
            {
                lock( _defaultStoreRootPathLock )
                {
                    p = _defaultStoreRootPath;
                    if( p == null )
                    {
                        p = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify );
                        p = new NormalizedPath( Path.Combine( p, "CK-AppIdentity" ) ).Path;
                        Volatile.Write( ref _defaultStoreRootPath, p );
                    }
                }
            }
            return p;
        }
        set
        {
            Throw.CheckArgument( "The default store root path must be a valid fully qualified path.",
                                 FileUtil.IndexOfInvalidPathChars( value.Path ) < 0 && Path.IsPathFullyQualified( value.Path ) );
            lock( _defaultStoreRootPathLock )
            {
                var p = _defaultStoreRootPath;
                if( p == null )
                {
                    Volatile.Write( ref _defaultStoreRootPath, value.Path );
                }
                else if( p != value.Path )
                {
                    Throw.InvalidOperationException( $"The DefaultStoreRootPath is already settled to '{p}', it can't be changed to '{value}'." );
                }
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
    /// <param name="domainName">
    /// Domain name. Defaults to <see cref="DefaultRootDomainName"/>: it can't be "External" or "Undefined" (an external system).
    /// </param>
    /// <param name="partyName">Party name (the leading '$' is optional).</param>
    /// <param name="environmentName">Environment name (must start with a '#', "#Development" is normalized to "#Dev").</param>
    /// <param name="storeRootPath">
    /// Optional store root path. Defaults to <see cref="DefaultStoreRootPath"/> (a private store).
    /// When specified, the store is shared (see <see cref="StoreRootPath"/>) and must be fully qualified.
    /// <para>
    /// The store root is created and checked here: a store writable by every local account is reported by a warning
    /// to the <see cref="ActivityMonitor.StaticLogger"/> and, in <see cref="StrictConfigurationMode"/>, this throws an
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// </param>
    /// <returns>An empty configuration.</returns>
    public static ApplicationIdentityServiceConfiguration CreateEmpty( string domainName = DefaultRootDomainName,
                                                                       string partyName = CoreApplicationIdentity.DefaultPartyName,
                                                                       string environmentName = CoreApplicationIdentity.DefaultEnvironmentName,
                                                                       NormalizedPath? storeRootPath = null )
    {
        if( storeRootPath.HasValue )
        {
            var store = storeRootPath.Value.Path;
            Throw.CheckArgument( "The store root path must be a valid fully qualified path.",
                                 FileUtil.IndexOfInvalidPathChars( store ) < 0 && Path.IsPathFullyQualified( store ) );
        }
        Throw.CheckNotNullArgument( domainName );
        Throw.CheckNotNullArgument( partyName );
        Throw.CheckNotNullArgument( environmentName );
        Throw.CheckArgument( CoreApplicationIdentity.IsValidDomainName( domainName ) );
        var firstSegment = domainName.Split( '/' )[0];
        Throw.CheckArgument( "The root domain name cannot be \"External\" or \"Undefined\": this denotes an external system.",
                             !firstSegment.Equals( ExternalDomainName, StringComparison.OrdinalIgnoreCase )
                             && !firstSegment.Equals( CoreApplicationIdentity.DefaultDomainName, StringComparison.OrdinalIgnoreCase ) );
        Throw.CheckArgument( partyName.Length > 0 && CoreApplicationIdentity.IsValidPartyName( partyName ) );
        Throw.CheckArgument( CoreApplicationIdentity.IsValidEnvironmentName( environmentName ) );
        if( environmentName.Equals( "#Development", StringComparison.OrdinalIgnoreCase ) )
        {
            environmentName = CoreApplicationIdentity.DefaultEnvironmentName;
        }
        if( partyName[0] != '$' ) partyName = '$' + partyName;
        var props = new InheritedConfigurationProps( ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty, AssemblyConfiguration.Empty );
        var c = new MutableConfigurationSection( "CK-AppIdentity" );
        var config = new ApplicationIdentityServiceConfiguration( new ImmutableConfigurationSection( c ),
                                                                  domainName,
                                                                  $"{domainName}/{partyName}/{environmentName}",
                                                                  storeRootPath,
                                                                  ref props );
        // Same check as HandleStorePath, without a monitor.
        if( !config._storeFileSystem.Initialize( ActivityMonitor.StaticLogger ) && config.StrictConfigurationMode )
        {
            Throw.InvalidOperationException( $"Store '{config.StoreRootPath}' is not secure (see the logs) and StrictConfigurationMode is true." );
        }
        return config;
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
        if( string.IsNullOrWhiteSpace( env ) || env.Equals( Environments.Development, StringComparison.OrdinalIgnoreCase ) )
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
        return Create( monitor, configuration, DefaultRootDomainName, hostEnvironment.ApplicationName, env );
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

    /// <summary>
    /// Tries to create an <see cref="ApplicationIdentityServiceConfiguration"/> instance from a <see cref="IConfigurationSection"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The configuration section (typically named "CK-AppIdentity").</param>
    /// <param name="defaultDomainName">A valid domain name to use if the <paramref name="configuration"/> doesn't specify "DomainName".</param>
    /// <param name="defaultPartyName">A party name to use if the <paramref name="configuration"/> doesn't specify "PartyName".</param>
    /// <param name="defaultEnvironmentName">An environment name to use if the <paramref name="configuration"/> doesn't specify "EnvironmentName".</param>
    /// <returns>A valid instance on success, null on configuration error (this includes invalid default names).</returns>
    public static ApplicationIdentityServiceConfiguration? Create( IActivityMonitor monitor,
                                                                   IConfigurationSection configuration,
                                                                   string? defaultDomainName = null,
                                                                   string? defaultPartyName = null,
                                                                   string defaultEnvironmentName = CoreApplicationIdentity.DefaultEnvironmentName )
    {
        Throw.CheckNotNullArgument( defaultEnvironmentName );
        using var gLog = monitor.OpenInfo( "Creating root ApplicationIdentityServiceConfiguration service." );
        var root = configuration as ImmutableConfigurationSection ?? new ImmutableConfigurationSection( configuration );

        // The strict mode is known only once the names are read: warnings are tracked from the start
        // and the tracker is closed once the whole configuration (including the root "Local") has been analyzed.
        using var warnTracker = new WarnTracker( monitor );

        // ReadNames is strict. An empty default party name would denote a group: it is not a default for the root.
        bool nameSuccess = ReadNames( monitor, root,
                                      out var domainName, out var partyName, out var environmentName,
                                      defaultDomainName, string.IsNullOrEmpty( defaultPartyName ) ? null : defaultPartyName, defaultEnvironmentName );
        bool success = nameSuccess & InheritedConfigurationProps.TryCreate( monitor, root, out var props );

        Throw.DebugAssert( nameof( StrictConfigurationMode ) == "StrictConfigurationMode" );
        bool strictMode = environmentName != CoreApplicationIdentity.DefaultEnvironmentName;
        var sStrict = root.TryLookupSection( "StrictConfigurationMode" );
        if( sStrict != null && !bool.TryParse( sStrict.Value, out strictMode ) )
        {
            monitor.Error( $"StrictConfigurationMode, when defined, must be a 'true' or 'false' boolean." );
            success = false;
        }

        if( domainName == ExternalDomainName )
        {
            Throw.DebugAssert( CoreApplicationIdentity.DefaultDomainName == "Undefined" );
            monitor.Error( $"Root domain name cannot be \"External\" or \"Undefined\". This name denotes an external system." );
            success = false;
        }
        var fullName = partyName[0] == '$' ? $"{domainName}/{partyName}/{environmentName}" : $"{domainName}/${partyName}/{environmentName}";

        // Always try to create the parties even if success is already false: this enables
        // configuration errors to be fixed at once.
        var fullNameIndex = new Dictionary<string, ImmutableConfigurationSection>( StringComparer.OrdinalIgnoreCase );
        // The local party is a party: no remote can have its FullName (they would share the same store folder).
        if( nameSuccess ) fullNameIndex.Add( fullName, root );
        var parties = CreateParties( monitor, root.GetSection( "Parties" ), domainName, environmentName, ref props, fullNameIndex );
        success &= parties.HasValue;

        var store = HandleStorePath( monitor, configuration );
        success &= store != null;

        var trashBinRetention = DefaultTrashBinRetention;
        var sRetention = root[nameof( TrashBinRetention )];
        if( sRetention != null
            && (!TimeSpan.TryParse( sRetention, CultureInfo.InvariantCulture, out trashBinRetention ) || trashBinRetention < TimeSpan.Zero) )
        {
            monitor.Error( $"Invalid '{root.Path}:{nameof( TrashBinRetention )}': '{sRetention}' must be a positive TimeSpan (like \"7.00:00:00\" for 7 days)." );
            success = false;
        }

        // Success may become false if something fails in the local configuration.
        var localConfig = CreateLocalConfiguration( monitor, root, ref props, ref success );

        success &= warnTracker.Close( monitor, strictMode );
        if( !success )
        {
            monitor.CloseGroup( "Failed." );
            return null;
        }
        return new ApplicationIdentityServiceConfiguration( root, domainName, fullName, localConfig, strictMode, store!, ref parties, ref props )
        {
            _trashBinRetention = trashBinRetention
        };

        static StoreFileSystem? HandleStorePath( IActivityMonitor monitor, IConfigurationSection configuration )
        {
            var store = configuration[nameof( StoreRootPath )]?.Trim();
            bool isPrivate = string.IsNullOrEmpty( store );
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
            var fileSystem = new StoreFileSystem( store, isPrivate );
            try
            {
                // Warnings are emitted here, in the scope of the StrictConfigurationMode tracker.
                fileSystem.Initialize( monitor );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Unable to create store directory '{store}'.", ex );
                return null;
            }
            return fileSystem;
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
        bool nameSuccess = ReadNames( monitor, configuration,
                                      out var domainName, out var partyName, out var environmentName,
                                      inhDomainName, "", inhEnvironmentName );
        // Always try to propagate the inherited props.
        bool success = nameSuccess & InheritedConfigurationProps.TryCreate( monitor, inhProps, configuration, out var props );
        // If a PartyName is not defined, it is a pure group. We handle it recursively, handling inherited names and properties.
        if( partyName.Length == 0 )
        {
            using var gLog = monitor.OpenInfo( $"Party group found '{partiesSection.Path}'." );
            int count = partyCollector.Count;
            // Only the "Parties" of a group are parties: its other keys (inherited configuration, "Local", etc.)
            // must not be read as parties.
            bool hasParties = false;
            foreach( var c in partiesSection.GetChildren() )
            {
                hasParties = true;
                success &= ReadParties( monitor, c, domainName, environmentName, ref props, ref partyCollector, fullNameIndex );
            }
            if( !hasParties )
            {
                monitor.Warn( $"'{configuration.Path}' has no PartyName (nor FullName) and no Parties: it is ignored." );
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
            if( partiesSection.Exists() )
            {
                monitor.Warn( $"'{partiesSection.Path}' is ignored: '{fullName}' is a remote party, only groups and tenant domains have Parties." );
            }
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
