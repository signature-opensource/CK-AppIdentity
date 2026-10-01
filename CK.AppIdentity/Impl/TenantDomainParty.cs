using CK.Core;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

sealed class TenantDomainParty : LocalParty, ITenantDomainParty, IOwnedPartyInternal
{
    // Created upfront (only dynamic parties can be destroyed): a concurrent DestroyAsync can't see it null.
    readonly TaskCompletionSource? _destroyTCS;
    // The remotes when SetDestroyed has been called: they are signaled with this domain.
    RemoteParty[]? _destroyedRemotes;
    int _isDestroyed;
    readonly bool _isDynamic;

    internal TenantDomainParty( TenantDomainPartyConfiguration configuration, bool isDynamic, ApplicationIdentityService root )
        : base( configuration,
                configuration.Remotes,
                configuration.LocalConfiguration,
                isDynamic,
                root )
    {
        _isDynamic = isDynamic;
        if( isDynamic ) _destroyTCS = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
    }

    public new TenantDomainPartyConfiguration Configuration => Unsafe.As<TenantDomainPartyConfiguration>( _configuration );

    ILocalParty IOwnedParty.Owner => ApplicationIdentityService;

    IApplicationIdentityService ITenantDomainParty.Owner => ApplicationIdentityService;

    public LocalParty Owner => ApplicationIdentityService;

    public bool IsDynamic => _isDynamic;

    public bool IsDestroyed => _isDestroyed != 0;

    public Task DestroyAsync()
    {
        SetDestroyed();
        Throw.DebugAssert( _destroyTCS != null );
        return _destroyTCS.Task;
    }

    public bool SetDestroyed()
    {
        Throw.CheckState( _isDynamic );
        if( Interlocked.CompareExchange( ref _isDestroyed, 1, 0 ) == 0 )
        {
            var remotes = _remotes;
            _destroyedRemotes = remotes;
            foreach( var r in remotes )
            {
                // Use the CAS check on destroy to prevent any
                // duplicate request but skip the IsDynamic check.
                r.DoSetDestroyed( false );
            }
            Owner.ApplicationIdentityService.Agent.OnDestroy( this );
            return true;
        }
        return false;
    }

    public void SignalDestroyed()
    {
        // The remotes are signaled by OnDestroyedAsync: this is for the case where the destruction
        // has not been handled (the agent is stopped or an error occurred).
        if( _destroyedRemotes != null )
        {
            foreach( var r in _destroyedRemotes ) r.SignalDestroyed();
        }
        _destroyTCS?.TrySetResult();
    }

    internal async Task OnDestroyedAsync( IActivityMonitor monitor )
    {
        // Signals the destruction completion of all subordinate remotes.
        // Clears the exposed remotes: when the event is raised, the destroyed
        // remotes must not appear in the Remotes.
        var remotes = Interlocked.Exchange( ref _remotes, Array.Empty<RemoteParty>() );
        foreach( var r in remotes )
        {
            try
            {
                // This raises an event for each remote.
                // Does this produces too much events (the bridge will relay the events to the root ApplicationIdentityService)?
                // It may be too verbose... but this is logically sound.
                await _remotesChanged.SafeRaiseAsync( monitor, r ).ConfigureAwait( false );
                await r.OnShutdownOrDestroyedAsync( monitor, true ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While destroying '{r}'.", ex );
            }
            finally
            {
                r.SignalDestroyed();
            }
        }
    }

    internal override async ValueTask OnShutdownOrDestroyedAsync( IActivityMonitor monitor, bool isDestroyed )
    {
        _remotesChangedBridge.Dispose();
        await base.OnShutdownOrDestroyedAsync( monitor, isDestroyed ).ConfigureAwait( false );
    }
}
