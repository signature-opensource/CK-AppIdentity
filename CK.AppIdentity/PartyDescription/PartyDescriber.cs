using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// The description of the parties, intrinsic to the <see cref="ApplicationIdentityService"/>: it sets the
/// <see cref="IParty.Description"/> of the local parties and loads the last known description of the remotes. The
/// <see cref="PartyDescription.Roles"/> of a local party are the asymmetric features it is.
/// <list type="bullet">
///   <item>
///   The description of a local party (the root and the tenant domains alike) starts with its "Local:PublicUrl"
///   configuration (an invalid one fails the setup), then each feature driver contributes to it, in the order of the
///   drivers (<see cref="ApplicationIdentityFeatureDriver.ContributeDescription(ILocalParty, PartyDescription.Builder)"/>):
///   the URL of the HTTP endpoints when "Local:PublicUrl" is not configured, the client applications... It is computed
///   once all the drivers have set up the party, then each time a driver signals a change
///   (<see cref="ILocalParty.DescriptionChanged"/> is raised when it changed).
///   </item>
///   <item>
///   A remote's description is its last announcement (<see cref="IRemoteParty.SetDescription"/>), persisted in its
///   folder of the shared store: it is read back when the remote is set up.
///   </item>
/// </list>
/// </summary>
sealed class PartyDescriber
{
    readonly ApplicationIdentityService _s;
    readonly SemaphoreSlim _lock;
    volatile bool _ready;

    public PartyDescriber( ApplicationIdentityService s )
    {
        _s = s;
        _lock = new SemaphoreSlim( 1 );
    }

    // Before the drivers set up the parties: checks the public URL of the local parties and loads the last known
    // description of the remotes.
    public bool Prepare( IActivityMonitor monitor, IEnumerable<ILocalParty> locals, IEnumerable<IRemoteParty> remotes )
    {
        bool success = true;
        foreach( var local in locals )
        {
            success &= TryReadPublicUrl( monitor, local, out _ );
        }
        foreach( var remote in remotes )
        {
            ((RemoteParty)remote).LoadDescription( monitor );
        }
        return success;
    }

    // Once the drivers have set up the initial parties.
    public async Task DescribeInitialAsync( IActivityMonitor monitor, IEnumerable<ILocalParty> locals )
    {
        await DescribeAsync( monitor, locals ).ConfigureAwait( false );
        _ready = true;
    }

    public Task RefreshAsync( IActivityMonitor monitor, ILocalParty? party )
    {
        if( !_ready ) return Task.CompletedTask;
        IEnumerable<ILocalParty> locals = party != null
                                            ? [party]
                                            : ((IEnumerable<ILocalParty>)_s.TenantDomains).Prepend( _s );
        return DescribeAsync( monitor, locals );
    }

    static bool TryReadPublicUrl( IActivityMonitor monitor, ILocalParty local, out Uri? url )
    {
        url = null;
        var value = local.LocalConfiguration.Configuration[ApplicationIdentityLocalConfiguration.PublicUrlKey];
        if( value == null ) return true;
        if( PartyDescription.TryParsePublicUrl( value, out url, out var error ) ) return true;
        monitor.Error( $"Invalid \"Local:{ApplicationIdentityLocalConfiguration.PublicUrlKey}\" of '{local.FullName}': {error}" );
        return false;
    }

    public async Task DescribeAsync( IActivityMonitor monitor, IEnumerable<ILocalParty> locals )
    {
        await _lock.WaitAsync().ConfigureAwait( false );
        try
        {
            foreach( var local in locals )
            {
                if( local is IOwnedParty { IsDestroyed: true } ) continue;
                // The configuration first (checked by the setup): a contribution sets the public URL only when it is not configured.
                TryReadPublicUrl( monitor, local, out var url );
                var builder = new PartyDescription.Builder { PublicUrl = url };
                foreach( var d in _s._builders )
                {
                    d.ContributeDescription( local, builder );
                }
                var description = builder.Build().WithRoles( _s.GetRoles( local ) );
                await ((LocalParty)local).SetLocalDescriptionAsync( monitor, description ).ConfigureAwait( false );
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
