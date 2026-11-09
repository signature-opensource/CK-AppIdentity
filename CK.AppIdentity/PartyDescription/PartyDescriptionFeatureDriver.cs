using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// The "PartyDescription" static feature: it sets the <see cref="IParty.Description"/> of the local parties and loads the
/// last known description of the remotes. The <see cref="PartyDescription.Roles"/> of a local party are the asymmetric
/// features it is: a remote to which a local party relays an asymmetric feature must announce it (an error when it
/// announces its description, a warning for its last known one).
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
public sealed class PartyDescriptionFeatureDriver : ApplicationIdentityStaticFeatureDriver
{
    /// <summary>
    /// The configuration key of the public URL, in the "Local" section of a local party.
    /// </summary>
    public const string PublicUrlKey = "PublicUrl";

    readonly SemaphoreSlim _lock;
    volatile bool _ready;

    /// <summary>
    /// Initializes a new <see cref="PartyDescriptionFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    public PartyDescriptionFeatureDriver( ApplicationIdentityService s )
        : base( s, isAllowedByDefault: true )
    {
        _lock = new SemaphoreSlim( 1 );
        s._descriptionDriver = this;
    }

    /// <inheritdoc />
    protected internal override Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        bool success = true;
        var locals = context.GetAllLocals().ToList();
        foreach( var local in locals )
        {
            success &= TryReadPublicUrl( context.Monitor, local, out _ );
        }
        foreach( var remote in context.GetAllRemotes() )
        {
            ((RemoteParty)remote).LoadDescription( context.Monitor );
        }
        if( success )
        {
            // Deferred: the drivers that come after this one set up the parties first.
            context.Trampoline.Add( () => DescribeInitialAsync( context.Monitor, locals ) );
        }
        return Task.FromResult( success );
    }

    /// <inheritdoc />
    protected override Task<bool> SetupDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        if( party is RemoteParty remote )
        {
            remote.LoadDescription( context.Monitor );
            return Task.FromResult( true );
        }
        if( party is TenantDomainParty domain )
        {
            if( !TryReadPublicUrl( context.Monitor, domain, out _ ) ) return Task.FromResult( false );
            foreach( var r in domain.Remotes ) ((RemoteParty)r).LoadDescription( context.Monitor );
            // Deferred: the drivers that come after this one set up the domain first.
            context.Trampoline.Add( () => DescribeAsync( context.Monitor, [domain] ) );
        }
        return Task.FromResult( true );
    }

    /// <inheritdoc />
    protected internal override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;

    async Task DescribeInitialAsync( IActivityMonitor monitor, IEnumerable<ILocalParty> locals )
    {
        await DescribeAsync( monitor, locals ).ConfigureAwait( false );
        _ready = true;
    }

    internal Task RefreshAsync( IActivityMonitor monitor, ILocalParty? party )
    {
        if( !_ready ) return Task.CompletedTask;
        IEnumerable<ILocalParty> locals = party != null
                                            ? [party]
                                            : ((IEnumerable<ILocalParty>)ApplicationIdentityService.TenantDomains).Prepend( ApplicationIdentityService );
        return DescribeAsync( monitor, locals );
    }

    static bool TryReadPublicUrl( IActivityMonitor monitor, ILocalParty local, out Uri? url )
    {
        url = null;
        var value = local.LocalConfiguration.Configuration[PublicUrlKey];
        if( value == null ) return true;
        if( PartyDescription.TryParsePublicUrl( value, out url, out var error ) ) return true;
        monitor.Error( $"Invalid \"Local:{PublicUrlKey}\" of '{local.FullName}': {error}" );
        return false;
    }

    async Task DescribeAsync( IActivityMonitor monitor, IEnumerable<ILocalParty> locals )
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
                // The first phase states the facts, the second one derives from them.
                foreach( var d in ApplicationIdentityService._builders )
                {
                    d.ContributeDescription( local, builder );
                }
                var derived = new PartyDescription.DerivedBuilder( builder );
                foreach( var d in ApplicationIdentityService._builders )
                {
                    d.ContributeDerivedDescription( local, derived );
                }
                var description = derived.Build().WithRoles( ApplicationIdentityService.GetRoles( local ) );
                await ((LocalParty)local).SetLocalDescriptionAsync( monitor, description ).ConfigureAwait( false );
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
