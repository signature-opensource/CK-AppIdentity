using CK.Core;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Base class of all identity objects: <see cref="CK.AppIdentity.ApplicationIdentityService"/>,
/// <see cref="TenantDomainParty"/> and <see cref="RemoteParty"/>.
/// </summary>
public abstract class ApplicationIdentityParty : IParty
{
    readonly ApplicationIdentityService _appIdentityService;
    private protected readonly ApplicationIdentityPartyConfiguration _configuration;
    readonly FileStore _sharedStore;
    object[] _features;
    int _partyActorId;

    // The folders of a dynamic party are not created by its constructor: they are created by the agent
    // (see CreateFolders) once the new parties have been checked.
    internal ApplicationIdentityParty( ApplicationIdentityPartyConfiguration configuration, bool isDynamic, ApplicationIdentityService? appIdentityService )
    {
        Throw.CheckNotNullArgument( configuration );
        _appIdentityService = appIdentityService ?? (ApplicationIdentityService)this;
        _configuration = configuration;
        _features = Array.Empty<object>();
        _sharedStore = new FileStore( ApplicationIdentityService.Configuration.StoreFileSystem,
                                      ApplicationIdentityService.ComputeSharedStorePath( configuration.FullName ),
                                      createFolder: !isDynamic );
    }

    /// <summary>
    /// Creates the folders of this party's stores (and of its remotes for a tenant domain).
    /// IO errors are thrown.
    /// </summary>
    /// <param name="created">Collects the topmost directories that have been created.</param>
    internal virtual void CreateFolders( List<NormalizedPath> created )
    {
        if( _sharedStore.CreateFolder() is NormalizedPath p ) created.Add( p );
    }

    IApplicationIdentityService IParty.ApplicationIdentityService => _appIdentityService;

    /// <inheritdoc cref="IParty.ApplicationIdentityService"/>
    public ApplicationIdentityService ApplicationIdentityService => _appIdentityService;

    /// <inheritdoc />
    public ApplicationIdentityPartyConfiguration Configuration => _configuration;

    /// <inheritdoc />
    public string DomainName => Configuration.DomainName;

    /// <inheritdoc />
    public string EnvironmentName => Configuration.EnvironmentName;

    /// <inheritdoc />
    public string PartyName => Configuration.PartyName;

    /// <inheritdoc />
    public NormalizedPath FullName => Configuration.FullName;

    /// <inheritdoc />
    public int PartyActorId => _partyActorId;

    /// <inheritdoc />
    public bool TrySetPartyActorId( IActivityMonitor monitor, int partyActorId )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckOutOfRangeArgument( partyActorId > 0 );
        var current = Interlocked.CompareExchange( ref _partyActorId, partyActorId, 0 );
        if( current == 0 )
        {
            monitor.Info( ActivityMonitor.Tags.SecurityCritical, $"PartyActorId of '{FullName}' is {partyActorId}." );
            return true;
        }
        if( current == partyActorId ) return true;
        monitor.Warn( ActivityMonitor.Tags.SecurityCritical, $"PartyActorId of '{FullName}' is {current}: it cannot be changed to {partyActorId}." );
        return false;
    }

    /// <inheritdoc />
    public IEnumerable<object> Features => _features;

    /// <inheritdoc />
    public void AddFeature( object feature )
    {
        Util.InterlockedAddUnique( ref _features, feature );
    }

    /// <inheritdoc />
    public T? GetFeature<T>() => _features.OfType<T>().FirstOrDefault();

    /// <inheritdoc />
    public T GetRequiredFeature<T>()
    {
        var feature = _features.OfType<T>().FirstOrDefault();
        if( feature == null ) Throw.InvalidOperationException( $"Unable to find a feature '{typeof( T ).ToCSharpName()}' in '{FullName}'." );
        return feature;
    }

    /// <inheritdoc />
    public IFileStore SharedFileStore => _sharedStore;

    /// <summary>
    /// Called by the service from the stopping agent when the service is being disposed
    /// or when this party has been destroyed.
    /// </summary>
    /// <param name="monitor">The agent's monitor.</param>
    /// <param name="isDestroyed">This party is being destroyed.</param>
    internal virtual ValueTask OnShutdownOrDestroyedAsync( IActivityMonitor monitor, bool isDestroyed )
    {
        // The trash bins are purged with the retention, even when this party is destroyed: its folder is
        // shared by the applications that use the same store and the party may still be alive in them.
        PurgeTrashBins( monitor, _appIdentityService.TrashBinLimitUtc );
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Purges the trash bins of this party's stores.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="olderThanUtc">The trash time limit.</param>
    /// <returns>The number of deleted trashed files.</returns>
    internal virtual int PurgeTrashBins( IActivityLineEmitter logger, DateTime olderThanUtc ) => _sharedStore.PurgeTrashBin( logger, olderThanUtc );

    /// <inheritdoc cref="IParty.ToString"/>
    public override string ToString() => FullName;

}
