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

namespace CK.AppIdentity
{
    /// <summary>
    /// Application identity micro agent.
    /// </summary>
    public sealed class AppIdentityAgent : MicroAgent
    {
        readonly ApplicationIdentityService _service;
        readonly IServiceProvider _serviceProvider;

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

        internal void Start() => Throw.CheckState( TryStart() == RunningStatus.Running );

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
            var lastRegistered = _serviceProvider.GetService( typeof(IApplicationIdentityFeatureDriver) );
            if( lastRegistered == null )
            {
                monitor.Warn( "No IApplicationIdentityFeatureDriver found in services. AppIdentity has no feature to manage." );
            }
            else
            {
                int count = _serviceProvider.GetServices<IApplicationIdentityFeatureDriver>().Count();
                if( count != _service._builders.Count )
                {
                    var missing = _serviceProvider.GetServices<IApplicationIdentityFeatureDriver>().Except( _service._builders ).Select( b => b.GetType() );
                    monitor.Error( $"Found {count} AppIdentityFeatureBuilder but only {_service._builders.Count} have registered themselves." +
                                    $" Missing registration for: {missing.Select( t => t.ToCSharpName() ).Concatenate()}." );
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
            using( monitor.OpenInfo( $"Starting {ToString()}: initializing '{_service._builders.Select( f => f.FeatureName ).Concatenate("', '")}' features." ) )
            {
                var initContext = new FeatureLifetimeContext( monitor, this, _service._builders );
                Exception? error = await initContext.ExecuteSetupAsync().ConfigureAwait( false );
                if( error == null ) _service._initialization.SetResult();
                else
                {
                    _service._initialization.SetException( error );
                    monitor.CloseGroup( "Failed." );
                }
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
                    else
                    {
                        init.Result.SetResult( false );
                    }
                    break;
            }
            return base.ExecuteTypedJobAsync( monitor, job );
        }

        async ValueTask HandleInitializeDynamicPartiesAsync( IActivityMonitor monitor, InitializeDynamicPartiesJob init )
        {
            int addedCount = init.Added.Count;
            using( monitor.OpenInfo( $"Initializing {addedCount} parties ({_service._builders.Count} feature builders)." ) )
            {
                bool success = true;
                // Setup a hash set with ALL the names, including the root application one.
                var existing = new HashSet<string>( _service.AllParties.Select( p => p.FullName.Path ).Prepend( _service.FullName.Path ), StringComparer.OrdinalIgnoreCase );
                Throw.DebugAssert( _service.AllParties.All( p => !p.IsDestroyed ), "We are in the Agent: operations are serialized: destroyed parties are not observable." );
                foreach( var p in init.Added.Parties )
                {
                    var newOne = p.FullName.Path;
                    Throw.DebugAssert( init.Added.Parties.SingleOrDefault( a => a.FullName.Path.Equals( p.FullName, StringComparison.OrdinalIgnoreCase ) ) == p,
                                  "This has been checked when building the configuration objects: there is no duplicates in the configuration." );
                    if( existing.Contains( newOne ) )
                    {
                        monitor.Error( $"Party '{newOne}' already exists. A party must first be destroyed before being added again." );
                        success = false;
                    }
                }
                // If a clash occurs, we add nothing.
                if( success )
                {
                    foreach( var p in init.Added.Parties )
                    {
                        using( monitor.OpenInfo( $"Initializing dynamic party '{p}'." ) )
                        {
                            var context = new FeatureLifetimeContext( monitor, this, _service._builders );
                            // The new configured party is published on the second round of the OnSuccess trampoline.
                            context.Trampoline.OnSuccess( () =>
                            {
                                context.Trampoline.OnSuccess( () => _service.OnCreatedAsync( context.Monitor, p ) );
                            } );
                            if( await context.ExecuteSetupDynamicRemoteAsync( p ).ConfigureAwait( false ) != TrampolineResult.TotalSuccess )
                            {
                                success = false;
                                monitor.CloseGroup( "Failed." );
                                break;
                            }
                        }
                    }
                }
                if( !success ) monitor.CloseGroup( "Failed." );
                init.Result.SetResult( success );
            }
        }

        async ValueTask HandleDestroyAsync( IActivityMonitor monitor, IOwnedPartyInternal destroyed )
        {
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
    }

}
