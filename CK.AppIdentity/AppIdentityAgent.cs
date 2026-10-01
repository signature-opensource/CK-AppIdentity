using CK.Core;
using CK.PerfectEvent;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                var initContext = new FeatureLifetimeContext( monitor, this, _service._builders );
                error = await initContext.ExecuteSetupAsync().ConfigureAwait( false );
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
                _service._initialization.TrySetException( error );
                monitor.CloseGroup( "Failed." );
            }
        }
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
            if( !CheckNewParties( monitor, added ) )
            {
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
            if( setup.Count < added.Count )
            {
                // The parties that have been fully set up have never been published: they are torn down.
                for( int i = setup.Count - 1; i >= 0; --i )
                {
                    var p = setup[i];
                    using( monitor.OpenInfo( $"Tearing down dynamic party '{p}' that has been set up: another party of the same batch failed." ) )
                    {
                        var context = new FeatureLifetimeContext( monitor, this, _service._builders );
                        await context.ExecuteTeardownDynamicRemoteAsync( p ).ConfigureAwait( false );
                    }
                }
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
        // Remotes can be added to a tenant domain: it must be alive.
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
                // Enables the feature drivers to tear down any existing features, including the
                // subordinates remotes if this is a domain.
                var context = new FeatureLifetimeContext( monitor, this, _service._builders );
                await context.ExecuteTeardownDynamicRemoteAsync( destroyed ).ConfigureAwait( false );

                // The service routes the call to the LocalService (for a remote) or
                // its domains.
                await _service.OnDestroyedAsync( monitor, destroyed ).ConfigureAwait( false );
            }
        }
        finally
        {
            // Whatever happens, the awaiters of DestroyAsync are released (exactly once).
            destroyed.SignalDestroyed();
        }
    }
}
