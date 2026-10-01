using CK.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

sealed class RemoteParty : ApplicationIdentityParty, IRemoteParty, IOwnedPartyInternal
{
    LocalParty _owner;
    // Created upfront (only dynamic parties can be destroyed): a concurrent DestroyAsync can't see it null.
    readonly TaskCompletionSource? _destroyTCS;
    int _isDestroyed;
    readonly bool _isDynamic;

    internal RemoteParty( RemotePartyConfiguration configuration, LocalParty owner, bool isDynamic )
        : base( configuration, isDynamic, owner.ApplicationIdentityService )
    {
        _owner = owner;
        _isDynamic = isDynamic;
        if( isDynamic ) _destroyTCS = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
    }

    public new RemotePartyConfiguration Configuration => Unsafe.As<RemotePartyConfiguration>( _configuration );

    public bool IsExternalParty => Configuration.IsExternalParty;

    public string? Address => Configuration.Address;

    public bool IsDynamic => _isDynamic;

    public bool IsDestroyed => _isDestroyed != 0;

    ILocalParty IOwnedParty.Owner => _owner;

    public LocalParty Owner => _owner;

    public Task DestroyAsync()
    {
        SetDestroyed();
        Throw.DebugAssert( _destroyTCS != null );
        return _destroyTCS.Task;
    }

    public bool SetDestroyed()
    {
        Throw.CheckState( IsDynamic );
        return DoSetDestroyed( true );
    }

    internal bool DoSetDestroyed( bool isTop )
    {
        Throw.DebugAssert( _destroyTCS != null, "Only dynamic parties can be destroyed (the remotes of a dynamic tenant domain are dynamic)." );
        if( Interlocked.CompareExchange( ref _isDestroyed, 1, 0 ) == 0 )
        {
            if( isTop ) Owner.ApplicationIdentityService.Agent.OnDestroy( this );
            return true;
        }
        return false;
    }

    public void SignalDestroyed() => _destroyTCS?.TrySetResult();
}
