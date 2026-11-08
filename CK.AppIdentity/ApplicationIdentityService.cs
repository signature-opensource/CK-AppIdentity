using CK.Core;
using CK.PerfectEvent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Singleton hosted service that is the local party and the root collection of <see cref="IOwnedParty"/>.
/// <para>
/// When used in a standard <see cref="IHost"/>, we exploit the extended <see cref="IHostedLifecycleService"/> to
/// start/stop the micro-agent and setup/teardown features in parallel with other <see cref="IHostedService"/>.
/// </para>
/// </summary>
public sealed partial class ApplicationIdentityService : LocalParty, IApplicationIdentityService, IHostedLifecycleService, IAsyncDisposable
{
    readonly AppIdentityAgent _agent;
    internal readonly List<ApplicationIdentityFeatureDriver> _builders;
    // Set by the PartyDescriptionFeatureDriver's constructor.
    internal PartyDescriptionFeatureDriver? _descriptionDriver;
    internal TaskCompletionSource _initialization;
    readonly internal PerfectEventSender<IOwnedParty> _allPartyChanged;
    internal TenantDomainParty[] _domains;
    readonly ISystemClock _systemClock;
    internal readonly PerfectEventSender<int> _heartbeat;
    // The host lifecycle of the static features: started (and stopped) once.
    readonly object _hostLock;
    Task? _hostStart;
    Task? _hostStop;
    readonly CancellationTokenSource _hostStopping;
    readonly List<ApplicationIdentityStaticFeatureDriver> _hostStarted;

    /// <summary>
    /// Initializes a new <see cref="ApplicationIdentityService"/> bound to a required configuration.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="serviceProvider">The application service provider.</param>
    public ApplicationIdentityService( ApplicationIdentityServiceConfiguration configuration, IServiceProvider serviceProvider )
        : base( configuration, configuration.Remotes, configuration.LocalConfiguration, false, null )
    {
        Throw.CheckNotNullArgument( serviceProvider );
        _builders = new List<ApplicationIdentityFeatureDriver>();
        _initialization = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        _systemClock = serviceProvider.GetService<ISystemClock>() ?? DefaultClock;
        _agent = new AppIdentityAgent( this, serviceProvider, _systemClock.HeartBeatPeriod );
        _heartbeat = new PerfectEventSender<int>();
        _allPartyChanged = (PerfectEventSender<IOwnedParty>)_remotesChangedBridge.Target;
        _domains = configuration.TenantDomains.Select( c => new TenantDomainParty( c, false, this ) ).ToArray();
        _hostLock = new object();
        _hostStarted = new List<ApplicationIdentityStaticFeatureDriver>();
        _hostStopping = new CancellationTokenSource();
    }

    internal NormalizedPath ComputeSharedStorePath( NormalizedPath fullName )
    {
        Throw.DebugAssert( fullName.Parts.Count >= 2 && fullName.LastPart[0] == '#' );
        var env = fullName.LastPart;
        var p = fullName.Path;
        return Configuration.StoreRootPath.Combine( $"{env}/{p.AsSpan( 0, p.Length - env.Length - 1 )}" );
    }

    internal AppIdentityAgent Agent => _agent;

    /// <summary>
    /// Gets the trash time before which trashed files are deleted.
    /// The trash time is the actual time (the file system's one), not the <see cref="SystemClock"/>.
    /// </summary>
    internal DateTime TrashBinLimitUtc => DateTime.UtcNow - Configuration.TrashBinRetention;

    /// <summary>
    /// Purges the trash bins of all the parties (including this root one).
    /// </summary>
    /// <param name="monitor">The agent's monitor.</param>
    internal void PurgeAllTrashBins( IActivityMonitor monitor )
    {
        var limit = TrashBinLimitUtc;
        int count = PurgeTrashBins( monitor, limit );
        foreach( var p in AllParties )
        {
            count += Unsafe.As<ApplicationIdentityParty>( p ).PurgeTrashBins( monitor, limit );
        }
        if( count > 0 ) monitor.Info( $"{count} trashed files older than {Configuration.TrashBinRetention} have been deleted." );
    }

    /// <inheritdoc />
    public new ApplicationIdentityServiceConfiguration Configuration => Unsafe.As<ApplicationIdentityServiceConfiguration>( _configuration );

    /// <inheritdoc />
    public PerfectEvent<IOwnedParty> AnyPartyChanged => _allPartyChanged.PerfectEvent;

    /// <inheritdoc />
    public IReadOnlyCollection<ITenantDomainParty> TenantDomains => _domains;

    /// <inheritdoc />
    public IEnumerable<IRemoteParty> AllRemotes
    {
        get
        {
            foreach( var r in _remotes ) yield return r;
            foreach( var d in _domains )
            {
                foreach( var r in d.Remotes )
                {
                    yield return r;
                }
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<IOwnedParty> Parties => ((IEnumerable<IOwnedParty>)Remotes).Concat( TenantDomains );

    /// <inheritdoc />
    public IEnumerable<IOwnedParty> AllParties
    {
        get
        {
            foreach( var r in _remotes ) yield return r;
            foreach( var d in _domains )
            {
                yield return d;
                foreach( var r in d.Remotes )
                {
                    yield return r;
                }
            }
        }
    }

    /// <inheritdoc />
    public Task InitializationTask => _initialization.Task;

    /// <inheritdoc />
    public PerfectEvent<int> Heartbeat => _heartbeat.PerfectEvent;

    /// <inheritdoc />
    public Core.ISystemClock SystemClock => _systemClock;

    /// <inheritdoc />
    public Task<AddedDynamicParties?> AddPartiesAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration )
    {
        return AddDynamicPartiesAsync( monitor, configuration, false, false, this );
    }

    /// <inheritdoc />
    public async Task<ITenantDomainParty?> AddTenantDomainAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration )
    {
        return (await AddDynamicPartiesAsync( monitor, configuration, false, true, this ).ConfigureAwait( false ))?.Tenants.Single();
    }

    internal Task OnDestroyedAsync( IActivityMonitor monitor, IOwnedParty owned )
    {
        return owned switch
        {
            RemoteParty p => p.Owner.OnDestroyedRemoteAsync( monitor, p ),
            TenantDomainParty d => OnDestroyedDomainAsync( monitor, d ),
            _ => Throw.NotSupportedException<Task>()
        };
    }

    async Task OnDestroyedDomainAsync( IActivityMonitor monitor, TenantDomainParty d )
    {
        Util.InterlockedRemove( ref _domains, d );
        // This destroys the LocalParty's remotes (including calls to OnShutdownOrDestroyedAsync)
        // and clears the _remotes array.
        await d.OnDestroyedAsync( monitor ).ConfigureAwait( false );
        // We raise the event before the final destruction of the domain and its destroy signal
        // (the agent signals it). Subscribers' exceptions are logged: they must not prevent the destruction.
        await _allPartyChanged.SafeRaiseAsync( monitor, d ).ConfigureAwait( false );
        await d.OnShutdownOrDestroyedAsync( monitor, true ).ConfigureAwait( false );
    }

    internal Task OnCreatedAsync( IActivityMonitor monitor, IOwnedParty owned )
    {
        return owned switch
        {
            RemoteParty p => p.Owner.OnCreatedRemoteAsync( monitor, p ),
            TenantDomainParty d => OnCreatedDomainAsync( monitor, d ),
            _ => Throw.NotSupportedException<Task>()
        };
    }

    async Task OnCreatedDomainAsync( IActivityMonitor monitor, TenantDomainParty d )
    {
        Util.InterlockedAdd( ref _domains, d );
        // Makes the domain appear before its remotes.
        // Subscribers' exceptions are logged: the domain is created.
        await _allPartyChanged.SafeRaiseAsync( monitor, d ).ConfigureAwait( false );
        foreach( var r in d.Remotes )
        {
            // There's little chance that subscribers exist on the new remote
            // but it is cleaner to raise the event through it (the bridge will do its job).
            await d.RemotesChangedSender.SafeRaiseAsync( monitor, r ).ConfigureAwait( false );
        }
    }

    /// <summary>
    /// Starts this identity service agent and awaits the <see cref="InitializationTask"/>, then starts the static
    /// features (<see cref="ApplicationIdentityStaticFeatureDriver.OnHostStartedAsync"/>): this is what a host does
    /// (see <see cref="IHostedLifecycleService"/>).
    /// When the returned task is successfully completed, all configured features are initialized and available.
    /// <para>
    /// This can be called safely multiple times and never throws: when the start is refused, the returned
    /// task is faulted and when the service has been stopped before being started, it is canceled.
    /// </para>
    /// <para>
    /// <see cref="DisposeAsync"/> stops the static features before stopping the agent.
    /// </para>
    /// </summary>
    /// <returns>The initialization and start task.</returns>
    public Task StartAndInitializeAsync()
    {
        _agent.Start();
        return StartStaticFeaturesAsync( default );
    }

    // The roles of a local party: the asymmetric features it IS (their Local side is installed and the party allows them).
    internal IEnumerable<string> GetRoles( ILocalParty local )
    {
        return _builders.Where( d => d._asymmetric?.Local == d && d.IsAllowedFeature( local ) ).Select( d => d.FeatureName );
    }

    // A local party that relays an asymmetric feature to a remote (the Remote side is installed, the local party doesn't
    // allow the feature, the remote allows "XXXRemote") checks that the remote announces being it: the forgotten "XXX"
    // in the "Local" of the remote can only be seen here. An announcement is an error, the last known description
    // (it may be stale) a warning.
    internal void CheckRelayedRoles( IActivityMonitor monitor, RemoteParty remote, bool isAnnouncement )
    {
        var roles = remote.Description.Roles;
        foreach( var d in _builders )
        {
            if( d._asymmetric?.Remote != d
                || d.IsAllowedFeature( remote.Owner )
                || !d.IsAllowedFeature( remote )
                || roles.Contains( d.FeatureName ) )
            {
                continue;
            }
            var text = $"'{remote.Owner}' relays the '{d.FeatureName}' feature to '{remote}' (it allows '{d.GetFeatureName( remote )}') but '{remote}' "
                       + $"doesn't announce being the '{d.FeatureName}': its \"Local\" must allow '{d.FeatureName}' (or, when '{remote.Owner}' is the '{d.FeatureName}', "
                       + $"its \"Local\" must allow it).";
            if( isAnnouncement ) monitor.Error( text );
            else monitor.Warn( $"Last known description: {text}" );
        }
    }

    // Starts the static features once the initialization is done (once).
    Task StartStaticFeaturesAsync( CancellationToken cancellationToken )
    {
        lock( _hostLock )
        {
            // A stop before the start: nothing will start.
            return _hostStart ??= DoStartStaticFeaturesAsync( cancellationToken );
        }
    }

    async Task DoStartStaticFeaturesAsync( CancellationToken cancellationToken )
    {
        // A stop cancels a pending start (that may wait for the initialization).
        using var linked = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, _hostStopping.Token );
        cancellationToken = linked.Token;
        await _initialization.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
        var drivers = _builders.OfType<ApplicationIdentityStaticFeatureDriver>().ToList();
        if( drivers.Count == 0 ) return;
        var monitor = new ActivityMonitor( $"Starting the static features of '{FullName}'." );
        try
        {
            foreach( var d in drivers )
            {
                try
                {
                    await d.OnHostStartedAsync( monitor, cancellationToken ).ConfigureAwait( false );
                    _hostStarted.Add( d );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While starting the static feature '{d.FeatureName}'.", ex );
                    await StopStartedStaticFeaturesAsync( monitor, default ).ConfigureAwait( false );
                    throw;
                }
            }
        }
        finally
        {
            monitor.MonitorEnd();
        }
    }

    // Stops the static features (once), after their start (if any) completed.
    Task StopStaticFeaturesAsync( CancellationToken cancellationToken )
    {
        lock( _hostLock )
        {
            if( _hostStop == null )
            {
                var start = _hostStart;
                // A stop before the start: nothing will start. The start then reflects the initialization (canceled
                // when the agent is stopped before being started).
                _hostStart ??= _initialization.Task;
                _hostStopping.Cancel();
                _hostStop = DoStopStaticFeaturesAsync( start, cancellationToken );
            }
            return _hostStop;
        }
    }

    async Task DoStopStaticFeaturesAsync( Task? start, CancellationToken cancellationToken )
    {
        if( start != null )
        {
            // A failed start has already stopped what it started.
            try { await start.ConfigureAwait( false ); } catch { }
        }
        if( _hostStarted.Count == 0 ) return;
        var monitor = new ActivityMonitor( $"Stopping the static features of '{FullName}'." );
        try
        {
            await StopStartedStaticFeaturesAsync( monitor, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            monitor.MonitorEnd();
        }
    }

    async Task StopStartedStaticFeaturesAsync( IActivityMonitor monitor, CancellationToken cancellationToken )
    {
        for( int i = _hostStarted.Count - 1; i >= 0; --i )
        {
            var d = _hostStarted[i];
            try
            {
                await d.OnHostStoppingAsync( monitor, cancellationToken ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While stopping the static feature '{d.FeatureName}'.", ex );
            }
        }
        _hostStarted.Clear();
    }

    #region IHostedService

    // StartingAsync is called first: the agent starts and the features are set up in the background.
    Task IHostedLifecycleService.StartingAsync( System.Threading.CancellationToken cancellationToken ) => DoStartAsync( cancellationToken );

    // StartAsync is called next, let's start again (this enables psedo host code
    // to use IHostedService.StartAsync transparently).
    Task IHostedService.StartAsync( CancellationToken cancellationToken ) => DoStartAsync( cancellationToken );

    Task DoStartAsync( CancellationToken cancellationToken )
    {
        if( !cancellationToken.IsCancellationRequested )
        {
            // Let the feature initialization be done in the background, in parallel
            // with other hosted services.
            _agent.Start();
            // A refused start fails the host start immediately.
            if( _initialization.Task.IsFaulted ) return _initialization.Task;
        }
        return Task.CompletedTask;
    }

    // Called last: awaits the initialization, then starts the static features.
    // (A host that only calls IHostedService.StartAsync doesn't start them: use StartAndInitializeAsync.)
    Task IHostedLifecycleService.StartedAsync( CancellationToken cancellationToken ) => StartStaticFeaturesAsync( cancellationToken );

    Task IHostedLifecycleService.StoppingAsync( CancellationToken cancellationToken ) => DoStopAsync( cancellationToken );

    Task IHostedService.StopAsync( CancellationToken cancellationToken ) => DoStopAsync( cancellationToken );

    // Stops the static features (outside the agent's loop, the parties are still there), then the agent.
    async Task DoStopAsync( CancellationToken cancellationToken )
    {
        await StopStaticFeaturesAsync( cancellationToken ).ConfigureAwait( false );
        _agent.SendStop();
    }

    Task IHostedLifecycleService.StoppedAsync( CancellationToken cancellationToken ) => _agent.RunningTask.WaitAsync( cancellationToken );
    #endregion

    /// <summary>
    /// Disposes this application identity service: this stops the static features
    /// (<see cref="ApplicationIdentityStaticFeatureDriver.OnHostStoppingAsync"/>) and then the micro agent.
    /// </summary>
    /// <returns>The awaitable.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopStaticFeaturesAsync( default ).ConfigureAwait( false );
        _agent.SendStop();
        await _agent.RunningTask.ConfigureAwait( false );
    }

    /// <summary>
    /// Called by the agent when stopping: this service is being disposed.
    /// </summary>
    /// <param name="monitor">The agent's monitor.</param>
    internal async Task OnShutdownAsync( IActivityMonitor monitor )
    {
        // This shuts down this root party (its stores) and its remotes: one remote can't prevent
        // the others to be shut down.
        try
        {
            await OnShutdownOrDestroyedAsync( monitor, false ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While shutting down '{this}'.", ex );
        }
        foreach( var d in _domains )
        {
            try
            {
                await d.OnShutdownOrDestroyedAsync( monitor, false ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While shutting down '{d}'.", ex );
            }
        }
    }

    /// <summary>
    /// Overridden to return "Application: <see cref="ApplicationIdentityParty.FullName"/>".
    /// </summary>
    /// <returns>This application's full name.</returns>
    public override string ToString() => $"Application: {FullName}";

}
