using CK.Core;
using CK.PerfectEvent;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Common base for <see cref="ApplicationIdentityService"/> and <see cref="TenantDomainParty"/>.
/// </summary>
public abstract class LocalParty : ApplicationIdentityParty, ILocalParty
{
    private protected RemoteParty[] _remotes;
    internal readonly PerfectEventSender<IRemoteParty> _remotesChanged;
    readonly PerfectEventSender<ILocalParty> _descriptionChanged;
    readonly PerfectEventSender<IRemoteParty> _remoteDescriptionChanged;
    readonly ApplicationIdentityLocalConfiguration _localConfiguration;
    readonly FileStore _privateStore;
    readonly IReadOnlyList<RemotePartyTemplate> _remoteTemplates;
    // Changes in contained remotes are propagated to the root AllPartyChanged event.
    private protected readonly IBridge _remotesChangedBridge;

    internal LocalParty( ApplicationIdentityPartyConfiguration configuration,
                         IEnumerable<RemotePartyConfiguration> remotes,
                         IReadOnlyList<RemotePartyTemplate> remoteTemplates,
                         ApplicationIdentityLocalConfiguration localConfiguration,
                         bool isDynamic,
                         ApplicationIdentityService? appIdentityService )
        : base( configuration, isDynamic, appIdentityService )
    {
        _remotesChanged = new PerfectEventSender<IRemoteParty>();
        _descriptionChanged = new PerfectEventSender<ILocalParty>();
        _remoteDescriptionChanged = new PerfectEventSender<IRemoteParty>();
        _remotes = remotes.Select( c => new RemoteParty( c, this, isDynamic ) ).ToArray();
        _remoteTemplates = remoteTemplates;
        // Creates a relay for RemotesChanged from this group to the root's AllPartyChanged event.
        // Chicken and Egg here: if we are the root, create the AllPartiesChanged event instance
        // and bridge it.
        // The ApplicationIdentityService constructor will get the IBridge.Target. 
        var bridgeTarget = appIdentityService?._allPartyChanged ?? new PerfectEventSender<IOwnedParty>();
        _remotesChangedBridge = _remotesChanged.CreateBridge( bridgeTarget!, Unsafe.As<IOwnedParty> );
        _privateStore = new FileStore( ApplicationIdentityService.Configuration.StoreFileSystem,
                                       SharedFileStore.FolderPath.AppendPart( FileStore.LocalStoreName ),
                                       createFolder: !isDynamic );
        _localConfiguration = localConfiguration;
    }

    /// <inheritdoc />
    public PerfectEvent<IRemoteParty> AnyRemoteChanged => _remotesChanged.PerfectEvent;

    public PerfectEvent<ILocalParty> DescriptionChanged => _descriptionChanged.PerfectEvent;

    /// <inheritdoc />
    public PerfectEvent<IRemoteParty> RemoteDescriptionChanged => _remoteDescriptionChanged.PerfectEvent;

    // Called by the agent's loop (see RemoteParty.SetDescription): the subscribers' exceptions are logged.
    internal Task RaiseRemoteDescriptionChangedAsync( IActivityMonitor monitor, RemoteParty remote ) => _remoteDescriptionChanged.SafeRaiseAsync( monitor, remote );

    /// <summary>
    /// Sets the description of this local party (see <see cref="PartyDescriber"/>) and raises
    /// <see cref="DescriptionChanged"/> when it changed.
    /// </summary>
    internal async Task SetLocalDescriptionAsync( IActivityMonitor monitor, PartyDescription description )
    {
        if( DoSetDescription( description ) == null ) return;
        monitor.Info( $"Description of '{FullName}': {description}." );
        await _descriptionChanged.SafeRaiseAsync( monitor, this ).ConfigureAwait( false );
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IRemoteParty> Remotes => _remotes;

    /// <inheritdoc />
    public ApplicationIdentityLocalConfiguration LocalConfiguration => _localConfiguration;

    /// <inheritdoc />
    public IFileStore LocalFileStore => _privateStore;

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<IRemoteParty>?> AddMultipleRemotesAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration )
    {
        return (await AddDynamicPartiesAsync( monitor, configuration, false, false, null ).ConfigureAwait( false ))?.Remotes;
    }

    /// <inheritdoc />
    public async Task<IRemoteParty?> AddRemoteAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration )
    {
        return (await AddDynamicPartiesAsync( monitor, configuration, true, false, null ).ConfigureAwait( false ))?.Remotes.Single();
    }

    public IReadOnlyList<RemotePartyTemplate> RemoteTemplates => _remoteTemplates;

    /// <inheritdoc />
    public async Task<IRemoteParty?> TryAddTemplateInstanceAsync( IActivityMonitor monitor, NormalizedPath fullName )
    {
        Throw.CheckNotNullArgument( monitor );
        foreach( var t in _remoteTemplates )
        {
            var instance = t.MatchDynamic( fullName );
            if( instance == null ) continue;
            if( _remotes.Any( r => r.FullName.Path.Equals( fullName.Path, StringComparison.OrdinalIgnoreCase ) ) ) return null;
            var c = t.CreateInstance( monitor, instance );
            // The party name matches: the full name must be the one of the instance (its domain and environment).
            if( c == null || !c.FullName.Path.Equals( fullName.Path, StringComparison.OrdinalIgnoreCase ) ) continue;
            using( monitor.OpenInfo( $"'{fullName}' is a candidate dynamic instance of the template {t}." ) )
            {
                return await AddTemplateInstanceAsync( monitor, c ).ConfigureAwait( false );
            }
        }
        return null;
    }

    // Adds a dynamic instance of a template (a candidate, or an instance pinned in the store at start).
    internal async Task<IRemoteParty?> AddTemplateInstanceAsync( IActivityMonitor monitor, RemotePartyConfiguration c )
    {
        Throw.DebugAssert( c.IsDynamicInstance );
        if( this is TenantDomainParty { IsDestroyed: true } )
        {
            monitor.Error( $"Unable to add '{c.FullName}' to '{this}': it is destroyed." );
            return null;
        }
        var r = new RemoteParty( c, this, true );
        var added = new AddedDynamicParties( new[] { r }, Array.Empty<TenantDomainParty>() );
        return await ApplicationIdentityService.Agent.InitializeDynamicPartiesAsync( added ).ConfigureAwait( false ) ? r : null;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveTemplateInstanceAsync( IActivityMonitor monitor, IRemoteParty remote )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( remote );
        if( remote is not RemoteParty r || r.Owner != this || !r.Configuration.IsDynamicInstance )
        {
            monitor.Error( $"'{remote}' is not a dynamic instance of a template of '{this}': it can't be removed." );
            return false;
        }
        using( monitor.OpenInfo( $"Removing the dynamic instance '{r.FullName}' of the template {r.Configuration.Template}." ) )
        {
            if( !r.IsDestroyed ) await r.DestroyAsync().ConfigureAwait( false );
            TrashTrustFiles( monitor, r.SharedFileStore );
        }
        return true;
    }

    // Trashes the files that pin the identity of a remote in its folder (never the folder: in a shared store, it is also
    // the party's own folder, with its "-Local").
    internal static int TrashTrustFiles( IActivityMonitor monitor, IFileStore store )
    {
        if( !Directory.Exists( store.FolderPath ) ) return 0;
        int count = 0;
        foreach( var f in Directory.GetFiles( store.FolderPath, IFileStore.TrustFilePattern ) )
        {
            if( store.TryTrash( monitor, f ) ) ++count;
        }
        monitor.Info( $"{count} pinned identity file(s) trashed in '{store.FolderPath}'." );
        return count;
    }

    private protected async Task<AddedDynamicParties?> AddDynamicPartiesAsync( IActivityMonitor monitor,
                                                                               Action<MutableConfigurationSection> configuration,
                                                                               bool singleRemote,
                                                                               bool singleTenant,
                                                                               ApplicationIdentityService? withTenants )
    {
        Throw.CheckNotNullArgument( configuration );
        // Early check (the agent checks it again when adding the parties).
        if( this is TenantDomainParty { IsDestroyed: true } )
        {
            monitor.Error( $"Unable to add parties to '{this}': it is destroyed." );
            return null;
        }
        var c = Configuration.CreateDynamicRemoteConfiguration( monitor, configuration, ApplicationIdentityService.Configuration.StrictConfigurationMode );
        if( c == null ) return null;
        if( c.Value.Count == 0 ) return new AddedDynamicParties( Array.Empty<RemoteParty>(), Array.Empty<TenantDomainParty>() );
        if( singleTenant )
        {
            if( c.Value.Tenants.Count > 1 || c.Value.Remotes.Count > 0 )
            {
                Throw.ArgumentException( nameof( configuration ), $"A single tenant domain configuration is expected " +
                                                                  $"(found '{c.Value.Parties.Select( p => p.ToString() ).Concatenate( "', '" )}')." );
            }
        }
        if( singleRemote )
        {
            if( c.Value.Remotes.Count > 1 || c.Value.Tenants.Count > 0 )
            {
                Throw.ArgumentException( nameof( configuration ), $"A single remote configuration is expected " +
                                                                  $"(found '{c.Value.Parties.Select( p => p.ToString() ).Concatenate( "', '" )}')." );
            }
        }
        if( withTenants == null )
        {
            if( c.Value.Tenants.Count > 0 )
            {
                Throw.ArgumentException( nameof( configuration ), $"No tenant domain configuration is allowed " +
                                                                  $"(found '{c.Value.Tenants.Select( d => d.ToString() ).Concatenate( "', '" )}')." );
            }
        }
        RemoteParty[] r = c.Value.Remotes.Select( c => new RemoteParty( c, this, true ) ).ToArray();
        TenantDomainParty[] t = withTenants == null
                                ? Array.Empty<TenantDomainParty>()
                                : c.Value.Tenants.Select( c => new TenantDomainParty( c, true, withTenants ) ).ToArray();

        var added = new AddedDynamicParties( r, t );
        if( !await ApplicationIdentityService.Agent.InitializeDynamicPartiesAsync( added ).ConfigureAwait( false ) )
        {
            return null;
        }
        // The items are InterlockedAdded to the _remotes and service _domains only on success (and in the second
        // round of OnSuccess trampoline) by the agent (ApplicationIdentityService.OnCreatedAsync is called for each remote or domain).
        return added;
    }


    internal PerfectEventSender<IRemoteParty> RemotesChangedSender => _remotesChanged;

    internal override void CreateFolders( List<NormalizedPath> created )
    {
        base.CreateFolders( created );
        // Inside the shared store's folder: it is never a topmost created directory when the shared one has been created.
        if( _privateStore.CreateFolder() is NormalizedPath p ) created.Add( p );
        // The remotes of a dynamic tenant domain are created with it.
        foreach( var r in _remotes )
        {
            r.CreateFolders( created );
        }
    }

    internal override int PurgeTrashBins( IActivityLineEmitter logger, DateTime olderThanUtc )
    {
        return base.PurgeTrashBins( logger, olderThanUtc ) + _privateStore.PurgeTrashBin( logger, olderThanUtc );
    }

    internal async Task OnDestroyedRemoteAsync( IActivityMonitor monitor, RemoteParty p )
    {
        Util.InterlockedRemove( ref _remotes, p );
        // Subscribers' exceptions are logged: they must not prevent the destruction.
        await _remotesChanged.SafeRaiseAsync( monitor, p ).ConfigureAwait( false );
        await p.OnShutdownOrDestroyedAsync( monitor, true ).ConfigureAwait( false );
    }

    internal Task OnCreatedRemoteAsync( IActivityMonitor monitor, RemoteParty p )
    {
        Util.InterlockedAdd( ref _remotes, p );
        // A tenant domain destroyed concurrently (after the agent's last check) may have missed this remote when it
        // marked its remotes as destroyed: its destruction (handled after this) removes it, it must be marked too.
        if( this is TenantDomainParty { IsDestroyed: true } ) p.DoSetDestroyed( false );
        // Subscribers' exceptions are logged: the remote is created.
        return _remotesChanged.SafeRaiseAsync( monitor, p );
    }

    internal override async ValueTask OnShutdownOrDestroyedAsync( IActivityMonitor monitor, bool isDestroyed )
    {
        await base.OnShutdownOrDestroyedAsync( monitor, isDestroyed ).ConfigureAwait( false );
        Throw.DebugAssert( !isDestroyed || _remotes.Length == 0, "The ApplicationIdentityService is never destroyed, only shut down. " +
                                                            "Destroying applies only for the TenantDomainParty and its has already cleared the _remotes list." );
        foreach( var r in _remotes )
        {
            try
            {
                await r.OnShutdownOrDestroyedAsync( monitor, isDestroyed ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                // One party must not prevent the others to be shut down.
                monitor.Error( $"While shutting down '{r}'.", ex );
            }
        }
    }


}
