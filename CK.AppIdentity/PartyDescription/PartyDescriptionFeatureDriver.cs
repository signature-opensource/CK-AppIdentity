using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// The "PartyDescription" static feature: it sets the <see cref="IParty.Description"/> of the local parties and loads the
/// last known description of the remotes.
/// <list type="bullet">
///   <item>
///   A local party's public URL is its "Local:PublicUrl" configuration (an invalid one fails the setup). The root party's
///   description is completed by the <see cref="IPartyDescriptionContributor"/> of the packages (the URL of the HTTP
///   endpoints when "Local:PublicUrl" is not configured, the client applications): it is computed when the party is
///   set up, then each time a contribution changes once the host has started (<see cref="ILocalParty.DescriptionChanged"/>).
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

    readonly IReadOnlyList<IPartyDescriptionContributor> _contributors;
    readonly SemaphoreSlim _rootLock;
    Uri? _rootPublicUrl;

    /// <summary>
    /// Initializes a new <see cref="PartyDescriptionFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="contributors">The contributors to the root party's description.</param>
    public PartyDescriptionFeatureDriver( ApplicationIdentityService s, IEnumerable<IPartyDescriptionContributor> contributors )
        : base( s, isAllowedByDefault: true )
    {
        _contributors = contributors.ToArray();
        _rootLock = new SemaphoreSlim( 1 );
    }

    /// <inheritdoc />
    protected internal override async Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        bool success = true;
        foreach( var local in context.GetAllLocals() )
        {
            if( !TryReadPublicUrl( context.Monitor, local, out var url ) )
            {
                success = false;
                continue;
            }
            if( local == ApplicationIdentityService )
            {
                _rootPublicUrl = url;
                await SetRootDescriptionAsync( context.Monitor ).ConfigureAwait( false );
            }
            else
            {
                await ((LocalParty)local).SetLocalDescriptionAsync( context.Monitor, new PartyDescription( url, default ) ).ConfigureAwait( false );
            }
        }
        foreach( var remote in context.GetAllRemotes() )
        {
            ((RemoteParty)remote).LoadDescription( context.Monitor );
        }
        return success;
    }

    /// <inheritdoc />
    protected override async Task<bool> SetupDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        if( party is RemoteParty remote )
        {
            remote.LoadDescription( context.Monitor );
            return true;
        }
        if( party is TenantDomainParty domain )
        {
            if( !TryReadPublicUrl( context.Monitor, domain, out var url ) ) return false;
            await domain.SetLocalDescriptionAsync( context.Monitor, new PartyDescription( url, default ) ).ConfigureAwait( false );
            foreach( var r in domain.Remotes ) ((RemoteParty)r).LoadDescription( context.Monitor );
        }
        return true;
    }

    /// <inheritdoc />
    protected internal override async Task OnHostStartedAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        foreach( var c in _contributors )
        {
            c.Activate( monitor, OnContributionChanged );
        }
        // The contributions may have changed since the setup (the HTTP endpoints are started).
        await SetRootDescriptionAsync( monitor ).ConfigureAwait( false );
    }

    /// <inheritdoc />
    protected internal override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;

    static bool TryReadPublicUrl( IActivityMonitor monitor, ILocalParty local, out Uri? url )
    {
        url = null;
        var value = local.LocalConfiguration.Configuration[PublicUrlKey];
        if( value == null ) return true;
        if( PartyDescription.TryParsePublicUrl( value, out url, out var error ) ) return true;
        monitor.Error( $"Invalid \"Local:{PublicUrlKey}\" of '{local.FullName}': {error}" );
        return false;
    }

    async Task SetRootDescriptionAsync( IActivityMonitor monitor )
    {
        await _rootLock.WaitAsync().ConfigureAwait( false );
        try
        {
            // The configuration first: a contributor sets the public URL only when it is not configured.
            var builder = new PartyDescription.Builder { PublicUrl = _rootPublicUrl };
            foreach( var c in _contributors )
            {
                c.Contribute( builder );
            }
            await ApplicationIdentityService.SetLocalDescriptionAsync( monitor, builder.Build() ).ConfigureAwait( false );
        }
        finally
        {
            _rootLock.Release();
        }
    }

    void OnContributionChanged()
    {
        _ = Task.Run( async () =>
        {
            var monitor = new ActivityMonitor( nameof( PartyDescriptionFeatureDriver ) );
            try
            {
                await SetRootDescriptionAsync( monitor ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                monitor.Error( "While updating the description of the root party.", ex );
            }
            finally
            {
                monitor.MonitorEnd();
            }
        } );
    }
}
