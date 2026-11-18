using CK.Core;
using CK.PerfectEvent;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Application identity micro agent.
/// </summary>
public sealed class AppIdentityAgent : MicroAgent
{
    readonly ApplicationIdentityService _service;
    readonly IServiceProvider _serviceProvider;
    // The trash bins are purged at start and then regularly (from the heartbeat).
    static readonly TimeSpan _trashPurgePeriod = TimeSpan.FromHours( 6 );
    DateTime _nextTrashPurgeUtc;

    internal AppIdentityAgent( ApplicationIdentityService service, IServiceProvider serviceProvider, int heartBeatPeriod )
        : base( $"ApplicationIdentityService Agent for '{service}'", heartBeatPeriod )
    {
        Throw.CheckArgument( heartBeatPeriod <= 1000 );
        _service = service;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the service provider of the running application.
    /// </summary>
    public IServiceProvider ServiceProvider => _serviceProvider;

    /// <summary>
    /// Gets the <see cref="ApplicationIdentityService"/>.
    /// </summary>
    public ApplicationIdentityService ApplicationIdentityService => _service;

    /// <summary>
    /// Gets the <see cref="ApplicationIdentityService.ISystemClock"/>.
    /// </summary>
    public ApplicationIdentityService.ISystemClock SystemClock => Unsafe.As<ApplicationIdentityService.ISystemClock>( _service.SystemClock );

    /// <summary>
    /// Starts the agent. This never throws: when the start is refused (see <see cref="OnTryStart(IActivityMonitor)"/>),
    /// the <see cref="ApplicationIdentityService.InitializationTask"/> is faulted and the agent is stopped: it will never start.
    /// This does nothing if the agent is already running or stopped.
    /// </summary>
    internal void Start()
    {
        if( TryStart() == RunningStatus.WaitingForStart )
        {
            _service._initialization.TrySetException( new InvalidOperationException( $"{ToString()} refused to start (see the logs)." ) );
            SendStop();
        }
    }

    /// <summary>
    /// Cancels the <see cref="ApplicationIdentityService.InitializationTask"/>: the initialization will never happen
    /// (if the start has been refused, it is already faulted).
    /// </summary>
    protected override void OnStoppedBeforeStart() => _service._initialization.TrySetCanceled();

    /// <summary>
    /// Releases the awaiters of the jobs that will never be executed:
    /// a destroyed party is signaled (it has been shut down with the service) and the addition of dynamic parties fails.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="job">The rejected job.</param>
    protected override void OnRejectedTypedJob( IActivityLineEmitter logger, object job )
    {
        switch( job )
        {
            case IOwnedPartyInternal destroyed:
                destroyed.SignalDestroyed();
                break;
            case InitializeDynamicPartiesJob init:
                logger.Error( $"Unable to add parties '{init.Added.Parties.Select( p => p.ToString() ).Concatenate( "', '" )}': {ToString()} is stopped." );
                init.Result.TrySetResult( false );
                break;
        }
    }

    /// <summary>
    /// Ensures that all feature providers have been instantiated.
    /// </summary>
    /// <param name="monitor">The agent monitor.</param>
    /// <returns>True when all the features have registered themselves, false otherwise: the agent refuses to start.</returns>
    protected override bool OnTryStart( IActivityMonitor monitor )
    {
        // This ensures that all feature providers have been instantiated.
        // We now use the builders that have been registered in the service: they
        // are necessarily topologically ordered by their dependencies so the calls
        // to InitializeAsync follows the ordering.
        // First, we test whether at least one IApplicationIdentityFeatureDriver is registered (the last one: this is how the
        // .NET conformant DI works).
        // We do this only to kindly handle tests whit empty services provider that don't handle IEnumerable (like the SimpleServiceContainer).
        var lastRegistered = _serviceProvider.GetService( typeof( IApplicationIdentityFeatureDriver ) );
        if( lastRegistered == null )
        {
            monitor.Warn( "No IApplicationIdentityFeatureDriver found in services. AppIdentity has no feature to manage." );
        }
        else
        {
            var drivers = _serviceProvider.GetServices<IApplicationIdentityFeatureDriver>().ToList();
            Throw.CheckState( "There cannot be less IApplicationIdentityFeatureDriver service registrations than base AppIdentityFeatureBuilder ctor calls.",
                              drivers.Count >= _service._builders.Count );
            // But there can be more for 2 different reasons:
            //  - Multiple manual registrations (when CK AutoDI is not used) can lead to duplicated singleton type (because AddSingleton
            //    has been instead of TryAddSingleton).
            //  - IApplicationIdentityFeatureDriver NOT implemented by the AppIdentityFeatureBuilder base class.
            if( drivers.Count > _service._builders.Count )
            {
                var aliens = drivers.RemoveWhereAndReturnsRemoved( s => s is not ApplicationIdentityFeatureDriver ).ToList();
                if( aliens.Count > 0 )
                {
                    monitor.Error( $"IAppIdentityFeatureBuilder type '{aliens.Select( s => s.GetType().ToCSharpName() ).Concatenate( "', '")}' must inherit from CK.AppIdentity.ApplicationIdentityFeatureDriver base class." );
                    return false;
                }
                var duplicates = drivers.GroupBy( Util.FuncIdentity ).Where( g => g.Count() >= 2 ).ToList();

                monitor.Error( $"""
                    Found duplicated service registration for: {duplicates.Select( g => $"'{g.Key.GetType().ToCSharpName()}' ({g.Count()} registrations)" ).Concatenate()}.
                    AppIdentityFeatureBuilder are singletons, they must be registered only once. If you cannot find the duplicate site(s),
                    use the (unfortunately costly) TryAddSingleton instead of AddSingleton registration. 
                    """ );
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Setups all the features.
    /// </summary>
    /// <param name="monitor">The agent's monitor.</param>
    /// <returns>The awaitable.</returns>
    protected override async ValueTask OnStartAsync( IActivityMonitor monitor )
    {
        using( monitor.OpenInfo( $"Starting {ToString()}: initializing '{_service._builders.Select( f => f.FeatureName ).Concatenate( "', '" )}' features." ) )
        {
            Exception? error;
            try
            {
                if( !TryAcquirePartyLocks( monitor, _service.TenantDomains.Cast<LocalParty>().Prepend( _service ) ) )
                {
                    error = new CKException( "A local party of this application is already running in another process (see the logs)." );
                }
                else if( CheckFeatureKeys( monitor, _service.AllParties.Prepend<IParty>( _service ) )
                         && CheckPartyScopes( monitor, _service.TenantDomains.Prepend<ILocalParty>( _service ) ) )
                {
                    var initContext = new FeatureLifetimeContext( monitor, this, _service._builders );
                    error = await initContext.ExecuteSetupAsync().ConfigureAwait( false );
                    if( error == null ) await AddPinnedTemplateInstancesAsync( monitor ).ConfigureAwait( false );
                }
                else
                {
                    error = new CKException( "Unknown feature names declared by the parties (see the logs) and StrictConfigurationMode is true." );
                }
            }
            catch( Exception ex )
            {
                error = ex;
            }
            // The trash bins are purged once the features are set up: setup errors are not related.
            PurgeTrashBins( monitor );
            if( error == null ) _service._initialization.TrySetResult();
            else
            {
                // A failed start runs nothing (the drivers undo their setup on error): its parties can be run by another
                // process (or by a new start of this one) without waiting for this service to be stopped.
                foreach( var l in _service.TenantDomains.Cast<LocalParty>().Prepend( _service ) ) l.ReleasePartyLock();
                _service._initialization.TrySetException( error );
                monitor.CloseGroup( "Failed." );
            }
        }
    }

    // Before any setup (the drivers use the stores: the keys, the pins): a local party runs in one process at a time (see
    // LocalParty.TryAcquirePartyLock). All or nothing: on failure, the locks taken are released.
    static bool TryAcquirePartyLocks( IActivityMonitor monitor, IEnumerable<LocalParty> locals )
    {
        var locked = new List<LocalParty>();
        foreach( var l in locals )
        {
            if( !l.TryAcquirePartyLock( monitor ) )
            {
                foreach( var r in locked ) r.ReleasePartyLock();
                return false;
            }
            locked.Add( l );
        }
        return true;
    }

    void PurgeTrashBins( IActivityMonitor monitor )
    {
        _nextTrashPurgeUtc = DateTime.UtcNow + _trashPurgePeriod;
        try
        {
            _service.PurgeAllTrashBins( monitor );
        }
        catch( Exception ex )
        {
            monitor.Warn( "While purging the trash bins.", ex );
        }
    }

    /// <summary>
    /// Tears down all the features.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The awaitable.</returns>
    protected override async ValueTask OnStopAsync( IActivityMonitor monitor )
    {
        var context = new FeatureLifetimeContext( monitor, this, _service._builders );
        await context.ExecuteTeardownAsync().ConfigureAwait( false );
        await _service.OnShutdownAsync( monitor ).ConfigureAwait( false );
    }

    /// <summary>
    /// Raises the <see cref="ApplicationIdentityService.Heartbeat"/> event.
    /// </summary>
    /// <param name="monitor">The agent's monitor.</param>
    /// <param name="callCount">The current call count. Starts at 0.</param>
    /// <returns>The awaitable.</returns>
    protected override Task OnHeartbeatAsync( IActivityMonitor monitor, int callCount )
    {
        if( DateTime.UtcNow >= _nextTrashPurgeUtc ) PurgeTrashBins( monitor );
        // Not using SafeRaiseAsync: exceptions are caught by the MicroAgent.
        return _service._heartbeat.RaiseAsync( monitor, callCount );
    }

    record class InitializeDynamicPartiesJob( AddedDynamicParties Added, TaskCompletionSource<bool> Result );

    internal void OnDestroy( IOwnedPartyInternal owned ) => PushTypedJob( owned );

    record class RemoteDescriptionChangedJob( RemoteParty Remote );

    internal void OnRemoteDescriptionChanged( RemoteParty remote ) => PushTypedJob( new RemoteDescriptionChangedJob( remote ) );

    internal Task<bool> InitializeDynamicPartiesAsync( AddedDynamicParties parties )
    {
        var cts = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
        PushTypedJob( new InitializeDynamicPartiesJob( parties, cts ) );
        return cts.Task;
    }

    /// <summary>
    /// Overridden to handle <see cref="IOwnedParty.DestroyAsync"/> and dynamic
    /// initialization of parties.
    /// </summary>
    /// <param name="monitor">The agent's monitor.</param>
    /// <param name="job">The private job to execute.</param>
    /// <returns>The awaitable.</returns>
    protected override ValueTask ExecuteTypedJobAsync( IActivityMonitor monitor, object job )
    {
        switch( job )
        {
            case IOwnedPartyInternal destroyed: return HandleDestroyAsync( monitor, destroyed );
            case RemoteDescriptionChangedJob changed:
                return changed.Remote.IsDestroyed
                        ? default
                        : new ValueTask( changed.Remote.Owner.RaiseRemoteDescriptionChangedAsync( monitor, changed.Remote ) );
            case InitializeDynamicPartiesJob init:
                if( Status == RunningStatus.Running )
                {
                    return HandleInitializeDynamicPartiesAsync( monitor, init );
                }
                // The agent is stopping: the parties won't be set up.
                OnRejectedTypedJob( monitor, init );
                return default;
        }
        return base.ExecuteTypedJobAsync( monitor, job );
    }

    async ValueTask HandleInitializeDynamicPartiesAsync( IActivityMonitor monitor, InitializeDynamicPartiesJob init )
    {
        bool success = false;
        try
        {
            success = await DoInitializeDynamicPartiesAsync( monitor, init.Added ).ConfigureAwait( false );
        }
        finally
        {
            init.Result.TrySetResult( success );
        }
    }

    // All or nothing: the parties are published only once all of them have been successfully set up.
    async Task<bool> DoInitializeDynamicPartiesAsync( IActivityMonitor monitor, AddedDynamicParties added )
    {
        using( monitor.OpenInfo( $"Initializing {added.Count} parties ({_service._builders.Count} feature builders)." ) )
        {
            if( !CheckNewParties( monitor, added )
                || !CheckFeatureKeys( monitor, added.Remotes.Concat<IParty>( added.Tenants.SelectMany( t => t.Remotes.Prepend<IParty>( t ) ) ) )
                || !CheckPartyScopes( monitor, added.Tenants ) )
            {
                monitor.CloseGroup( "Failed." );
                return false;
            }
            // The folders are created once the parties have been checked and before their setup (the drivers
            // can use the stores). Only the folders that are created here are removed on failure, and only if they
            // are empty: an existing folder is shared by the applications that use the same store and holds
            // the pinned identity of the party.
            var createdFolders = new List<NormalizedPath>();
            try
            {
                foreach( var p in added.Parties )
                {
                    Unsafe.As<ApplicationIdentityParty>( p ).CreateFolders( createdFolders );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( "While creating the folders of the new parties.", ex );
                DeleteCreatedFolders( monitor, createdFolders );
                monitor.CloseGroup( "Failed." );
                return false;
            }
            // A new tenant domain is locked before its setup, as the configured ones at start. Its lock file stays in its
            // "-Local" (a lock file is never deleted): on failure, its created folder is kept, with only this file.
            if( !TryAcquirePartyLocks( monitor, added.Tenants.Cast<LocalParty>() ) )
            {
                DeleteCreatedFolders( monitor, createdFolders );
                monitor.CloseGroup( "Failed." );
                return false;
            }
            var setup = new List<IOwnedParty>( added.Count );
            foreach( var p in added.Parties )
            {
                using( monitor.OpenInfo( $"Initializing dynamic party '{p}'." ) )
                {
                    var context = new FeatureLifetimeContext( monitor, this, _service._builders );
                    // Only an error matters: the success handlers exceptions are logged and ignored.
                    // On error, the drivers' OnError handlers registered on the trampoline undo their work.
                    var result = await context.ExecuteSetupDynamicRemoteAsync( p ).ConfigureAwait( false );
                    if( (result & TrampolineResult.Error) != 0 )
                    {
                        monitor.CloseGroup( "Failed." );
                        break;
                    }
                    setup.Add( p );
                }
            }
            // A tenant domain can be destroyed while its new remotes are set up (its destruction is handled after this job):
            // a remote is never published in a destroyed domain.
            bool domainDestroyed = setup.Count == added.Count && !CheckLiveDomains( monitor, added );
            if( setup.Count < added.Count || domainDestroyed )
            {
                // The parties that have been fully set up have never been published: they are torn down.
                for( int i = setup.Count - 1; i >= 0; --i )
                {
                    var p = setup[i];
                    using( monitor.OpenInfo( domainDestroyed
                                                ? $"Tearing down dynamic party '{p}' that has been set up: a tenant domain of the batch is destroyed."
                                                : $"Tearing down dynamic party '{p}' that has been set up: another party of the same batch failed." ) )
                    {
                        var context = new FeatureLifetimeContext( monitor, this, _service._builders );
                        await context.ExecuteTeardownDynamicRemoteAsync( p ).ConfigureAwait( false );
                    }
                }
                foreach( var t in added.Tenants.Cast<LocalParty>() ) t.ReleasePartyLock();
                DeleteCreatedFolders( monitor, createdFolders );
                monitor.CloseGroup( "Failed." );
                return false;
            }
            foreach( var p in added.Parties )
            {
                // This never throws: the events are safely raised.
                await _service.OnCreatedAsync( monitor, p ).ConfigureAwait( false );
            }
            return true;
        }
    }

    // The dynamic instances of the Opened remote templates that have been approved: their identity is pinned in their
    // folder of the store (derived, nothing else is stored). An instance above the bound of its template (the bound has
    // been lowered by the configuration: the newer decision) has its pins trashed. A failing instance is logged: it doesn't
    // fail the start.
    async Task AddPinnedTemplateInstancesAsync( IActivityMonitor monitor )
    {
        foreach( var local in _service.TenantDomains.Cast<LocalParty>().Prepend<LocalParty>( _service ) )
        {
            foreach( var t in local.RemoteTemplates )
            {
                var instances = t.Instances;
                // Any instance gives the folder of the instances: the one of their domain and environment.
                var probe = t.CreateInstance( monitor, instances.Count < instances.MaxInstanceCount
                                                        ? instances.CreateDynamic( instances.Count + 1 )
                                                        : instances[0] );
                if( probe == null ) continue;
                var parent = _service.ComputeSharedStorePath( probe.FullName ).RemoveLastPart();
                if( !Directory.Exists( parent ) ) continue;
                foreach( var d in Directory.GetDirectories( parent ) )
                {
                    NormalizedPath folder = d;
                    if( !ConfigurationTemplate.TryMatchIndex( t.PartyNamePattern, folder.LastPart, out var index )
                        || index <= instances.Count
                        || !Directory.EnumerateFiles( folder, IFileStore.TrustFilePattern ).Any() )
                    {
                        continue;
                    }
                    if( !instances.IsDynamicIndex( index ) )
                    {
                        monitor.Warn( $"'{folder}' pins the identity of the instance {index} of the template {t}, above its \"{ConfigurationTemplate.MaxInstanceCountKey}\" ({instances.MaxInstanceCount}): the pins are trashed." );
                        LocalParty.TrashTrustFiles( monitor, new FileStore( _service.Configuration.StoreFileSystem, folder, createFolder: false ) );
                        continue;
                    }
                    var c = t.CreateInstance( monitor, instances.CreateDynamic( index ) );
                    if( c == null
                        || _service.ComputeSharedStorePath( c.FullName ) != folder
                        || local.Remotes.Any( r => r.FullName.Path.Equals( c.FullName.Path, StringComparison.OrdinalIgnoreCase ) ) )
                    {
                        continue;
                    }
                    using( monitor.OpenInfo( $"Creating the approved dynamic instance '{c.FullName}' of the template {t}." ) )
                    {
                        var r = new RemoteParty( c, local, true );
                        if( !await DoInitializeDynamicPartiesAsync( monitor, new AddedDynamicParties( new[] { r }, Array.Empty<TenantDomainParty>() ) ).ConfigureAwait( false ) )
                        {
                            monitor.CloseGroup( "Failed." );
                        }
                    }
                }
            }
        }
    }

    static void DeleteCreatedFolders( IActivityMonitor monitor, List<NormalizedPath> createdFolders )
    {
        for( int i = createdFolders.Count - 1; i >= 0; --i )
        {
            FileStore.DeleteFolderIfEmpty( monitor, createdFolders[i] );
        }
    }

    // The keys of a party (and of the "Defaults" above it) are its own keys or features: a feature key must be the name
    // of a registered feature (FeatureName, and FeatureName + "Remote" for an asymmetric feature), case insensitive. An
    // unknown key (a typo that would leave a feature allowed by default on, a setting misplaced at the root of a party
    // instead of its feature's section) is a warning, an error in StrictConfigurationMode.
    // The declarations follow the activation of their feature: false on a Required feature is an error, a redundant
    // true (ByDefault, Required) or false (Optional) is a warning.
    bool CheckFeatureKeys( IActivityMonitor monitor, IEnumerable<IParty> parties )
    {
        var known = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        foreach( var d in _service._builders )
        {
            known.Add( d.FeatureName );
            if( d.IsAsymmetric ) known.Add( d.FeatureName + "Remote" );
        }
        Dictionary<string, List<IParty>>? unknown = null;
        Dictionary<(string Key, string Text), List<IParty>>? redundant = null;
        Dictionary<string, List<IParty>>? requiredOff = null;
        Dictionary<string, List<IParty>>? notMember = null;
        foreach( var p in parties )
        {
            ApplicationIdentityConfiguration c = p is ILocalParty local ? local.LocalConfiguration : p.Configuration;
            foreach( var name in c.GetFeatureKeys() )
            {
                if( known.Contains( name ) ) continue;
                unknown ??= new Dictionary<string, List<IParty>>( StringComparer.OrdinalIgnoreCase );
                if( !unknown.TryGetValue( name, out var list ) ) unknown.Add( name, list = new List<IParty>() );
                list.Add( p );
            }
            // The types drive: a declared feature must be a member of the party's set (false is fine). The two sides of an
            // asymmetric feature read the same key ("XXXRemote" on a remote): one of them must be a member.
            foreach( var g in _service._builders.GroupBy( d => d.GetFeatureName( p ), StringComparer.OrdinalIgnoreCase ) )
            {
                var declaration = c.GetFeatureDeclarationSection( g.Key );
                if( declaration != null
                    && !string.Equals( declaration.Value, "false", StringComparison.OrdinalIgnoreCase )
                    && !g.Any( d => d.IsMember( p ) ) )
                {
                    notMember ??= new Dictionary<string, List<IParty>>( StringComparer.OrdinalIgnoreCase );
                    if( !notMember.TryGetValue( g.Key, out var list ) ) notMember.Add( g.Key, list = new List<IParty>() );
                    list.Add( p );
                }
            }
            foreach( var d in _service._builders )
            {
                var key = d.GetFeatureName( p );
                var declaration = c.GetFeatureDeclarationSection( key );
                if( declaration == null || !d.IsMember( p ) ) continue;
                var value = declaration.Value;
                if( value == null ) continue;
                bool isFalse = string.Equals( value, "false", StringComparison.OrdinalIgnoreCase );
                if( isFalse && d.Activation == TypeActivation.Required )
                {
                    requiredOff ??= new Dictionary<string, List<IParty>>( StringComparer.OrdinalIgnoreCase );
                    if( !requiredOff.TryGetValue( key, out var list ) ) requiredOff.Add( key, list = new List<IParty>() );
                    list.Add( p );
                }
                else if( isFalse ? d.Activation == TypeActivation.Optional : d.Activation != TypeActivation.Optional )
                {
                    var text = isFalse ? "false: the feature is optional, it is off unless declared" : $"true: the feature is {(d.Activation == TypeActivation.Required ? "required" : "allowed by default")}";
                    redundant ??= new Dictionary<(string, string), List<IParty>>();
                    if( !redundant.TryGetValue( (key, text), out var list ) ) redundant.Add( (key, text), list = new List<IParty>() );
                    list.Add( p );
                }
            }
        }
        bool success = true;
        if( notMember != null )
        {
            foreach( var (key, list) in notMember )
            {
                monitor.Error( $"The feature '{key}' is declared by '{list.Select( p => p.ToString() ).Concatenate( "', '" )}' but it is not a member of the type set of their party: the types drive, a key doesn't bring its feature in (add its type to the \"Types\" of the party)." );
            }
            success = false;
        }
        if( requiredOff != null )
        {
            foreach( var (key, list) in requiredOff )
            {
                monitor.Error( $"The feature '{key}' is required: it can't be turned off by '{list.Select( p => p.ToString() ).Concatenate( "', '" )}'." );
            }
            success = false;
        }
        if( redundant != null )
        {
            foreach( var ((key, text), list) in redundant )
            {
                monitor.Warn( $"Redundant \"{key}\": {text} ('{list.Select( p => p.ToString() ).Concatenate( "', '" )}')." );
            }
        }
        if( unknown == null ) return success;
        bool strict = _service.Configuration.StrictConfigurationMode;
        var knownNames = known.Order( StringComparer.Ordinal ).Concatenate( "', '" );
        foreach( var (name, list) in unknown )
        {
            var text = $"Unknown feature '{name}' declared by '{list.Select( p => p.ToString() ).Concatenate( "', '" )}' (the features are '{knownNames}').";
            if( strict ) monitor.Error( text + " StrictConfigurationMode is true." );
            else monitor.Warn( text );
        }
        return success && !strict;
    }

    // The scope of a local party is its allowed features: a required feature (or abstraction) must be allowed too, a
    // unique abstraction is implemented at most once (the same rules as the clients, checked at build time too).
    bool CheckPartyScopes( IActivityMonitor monitor, IEnumerable<ILocalParty> locals )
    {
        // Without feature driver, nothing to check (and there may be no directory).
        if( _service._builders.Count == 0 ) return true;
        bool success = true;
        var directory = _service.ComponentDirectory;
        foreach( var local in locals )
        {
            var scope = _service._builders.Where( d => d.IsAllowedFeature( local ) )
                                          .Select( d => directory.Find( d.ComponentType ) )
                                          .Where( i => i != null )
                                          .ToList();
            success &= PartyComponentDirectory.CheckScope( monitor, $"'{local}'", scope!, directory.IsMultipleAbstraction, _ => false );
        }
        return success;
    }

    bool CheckNewParties( IActivityMonitor monitor, AddedDynamicParties added )
    {
        bool success = true;
        // Setup a hash set with ALL the names, including the root application one.
        // A party being destroyed (IsDestroyed is true but it is still here) keeps its name until its
        // destruction is handled.
        var existing = new HashSet<string>( _service.AllParties.Select( p => p.FullName.Path ).Prepend( _service.FullName.Path ), StringComparer.OrdinalIgnoreCase );
        foreach( var p in added.Parties )
        {
            var newOne = p.FullName.Path;
            Throw.DebugAssert( added.Parties.SingleOrDefault( a => a.FullName.Path.Equals( p.FullName, StringComparison.OrdinalIgnoreCase ) ) == p,
                          "This has been checked when building the configuration objects: there is no duplicates in the configuration." );
            if( existing.Contains( newOne ) )
            {
                monitor.Error( $"Party '{newOne}' already exists. A party must first be destroyed before being added again." );
                success = false;
            }
        }
        return CheckLiveDomains( monitor, added ) && success;
    }

    // Remotes can be added to a tenant domain: it must be alive.
    bool CheckLiveDomains( IActivityMonitor monitor, AddedDynamicParties added )
    {
        bool success = true;
        foreach( var r in added.Remotes )
        {
            if( r.Owner is TenantDomainParty d && (d.IsDestroyed || !_service.TenantDomains.Contains( d )) )
            {
                monitor.Error( $"Unable to add remote '{r}': its tenant domain '{d}' is destroyed." );
                success = false;
            }
        }
        return success;
    }

    async ValueTask HandleDestroyAsync( IActivityMonitor monitor, IOwnedPartyInternal destroyed )
    {
        try
        {
            // A remote of a tenant domain may have been destroyed with its domain (when both are destroyed
            // concurrently, the domain may be handled first): there is nothing more to do.
            if( destroyed is RemoteParty r && !r.Owner.Remotes.Contains( r ) )
            {
                monitor.Trace( $"'{destroyed}' has already been destroyed with its tenant domain." );
                return;
            }
            using( monitor.OpenInfo( $"Destroying '{destroyed}'." ) )
            {
                // The folders of the destroyed parties (a domain's remotes are destroyed with it): captured before the
                // destruction clears the remotes of a domain.
                List<NormalizedPath> folders = destroyed is TenantDomainParty d
                                ? d.Remotes.Select( r => r.SharedFileStore.FolderPath ).Append( d.SharedFileStore.FolderPath ).ToList()
                                : [Unsafe.As<ApplicationIdentityParty>( destroyed ).SharedFileStore.FolderPath];

                // Enables the feature drivers to tear down any existing features, including the
                // subordinates remotes if this is a domain.
                var context = new FeatureLifetimeContext( monitor, this, _service._builders );
                await context.ExecuteTeardownDynamicRemoteAsync( destroyed ).ConfigureAwait( false );

                // The service routes the call to the LocalService (for a remote) or
                // its domains.
                await _service.OnDestroyedAsync( monitor, destroyed ).ConfigureAwait( false );

                // A folder that holds nothing is deleted: the one of a candidate that has never been approved leaves no
                // debris. A folder with a file is kept: a pin, a description, the "-Local" of a party that runs here
                // or in another application that shares the store (a write there creates the folder again: see FileStore).
                foreach( var f in folders )
                {
                    FileStore.DeleteFolderIfEmpty( monitor, f );
                }
            }
        }
        finally
        {
            // Whatever happens, the awaiters of DestroyAsync are released (exactly once).
            destroyed.SignalDestroyed();
        }
    }
}
