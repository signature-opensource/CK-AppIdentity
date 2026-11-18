using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CK.AppIdentity;

// The run-time tenant domains: the instances of the Opened tenant templates, created by the application. Everything here
// runs in the agent's loop: the creations, the moves and the removals are serialized (the first free index is computed
// with the creation).
public sealed partial class AppIdentityAgent
{
    /// <summary>
    /// The record of a run-time tenant domain, in its private store: <c>{ "Index": 3 }</c>. Its name is the one of its folder.
    /// </summary>
    internal const string InstanceFileName = "Instance.json";

    FileStore OpenLocalStore( NormalizedPath fullName )
    {
        return new FileStore( _service.Configuration.StoreFileSystem,
                              _service.ComputeSharedStorePath( fullName ).AppendPart( FileStore.LocalStoreName ),
                              createFolder: false );
    }

    static NormalizedPath RecordPath( IFileStore local ) => local.FolderPath.AppendPart( InstanceFileName );

    static bool TryReadRecord( IFileStore local, out int index )
    {
        index = 0;
        try
        {
            using var s = local.OpenReadStream( RecordPath( local ) );
            using var doc = JsonDocument.Parse( s );
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty( "Index", out var i )
                   && i.TryGetInt32( out index )
                   && index > 0;
        }
        catch( Exception ex ) when( ex is IOException or JsonException or UnauthorizedAccessException )
        {
            return false;
        }
    }

    static bool TryWriteRecord( IActivityMonitor monitor, IFileStore local, int index )
    {
        try
        {
            local.WriteAtomically( RecordPath( local ), s =>
            {
                using var w = new Utf8JsonWriter( s, new JsonWriterOptions { Indented = true } );
                w.WriteStartObject();
                w.WriteNumber( "Index", index );
                w.WriteEndObject();
            } );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While writing the record '{RecordPath( local )}'.", ex );
            return false;
        }
    }

    // The tenant domains of a template that are loaded (configured or run-time), but the excluded one.
    IEnumerable<TenantDomainParty> LoadedInstances( TenantDomainTemplate template, TenantDomainParty? except = null )
    {
        return _service._domains.Where( d => d != except && !d.IsDestroyed && d.Configuration.Template == template );
    }

    // The first free index of the run-time instances: no configured instance, no loaded tenant has it. An unloaded tenant
    // holds no index (its own is taken, or above the bound). 0 when there is none.
    int FindFreeIndex( TenantDomainTemplate template )
    {
        var used = LoadedInstances( template ).Select( d => d.Configuration.Instance!.Index ).ToHashSet();
        for( int i = template.Instances.Count + 1; i <= template.Instances.MaxInstanceCount; ++i )
        {
            if( !used.Contains( i ) ) return i;
        }
        return 0;
    }

    // Creates (and publishes) the tenant domain of an instance. The record is written last when writeRecord is true.
    async Task<TenantDomainParty?> CreateTenantInstanceAsync( IActivityMonitor monitor,
                                                              TenantDomainTemplate template,
                                                              ConfigurationInstance instance,
                                                              bool writeRecord )
    {
        var c = template.CreateInstance( monitor, instance );
        if( c == null ) return null;
        var t = new TenantDomainParty( c, true, _service );
        var added = new AddedDynamicParties( Array.Empty<RemoteParty>(), new[] { t } );
        Func<IActivityMonitor, bool>? beforePublish = writeRecord
                                                        ? m => TryWriteRecord( m, t.LocalFileStore, instance.Index )
                                                        : null;
        return await DoInitializeDynamicPartiesAsync( monitor, added, beforePublish ).ConfigureAwait( false ) ? t : null;
    }

    // At start, before the pinned remote instances: the run-time tenant domains whose record is in the store are created
    // again, or kept unloaded with their reason. The folders under the domain of a template that hold a record are the
    // run-time tenants (derived: nothing else is stored), in the order of their names.
    async Task AddCreatedTenantsAsync( IActivityMonitor monitor )
    {
        var unloaded = new List<UnloadedTenant>();
        foreach( var template in _service.Configuration.TenantTemplates )
        {
            // {StoreRoot}/{Env}/{DomainPrefix}: the folder of the domains of the instances.
            var prefixFolder = _service.ComputeSharedStorePath( template.GetFullName( "Probe" ) ).RemoveLastPart().RemoveLastPart();
            if( !Directory.Exists( prefixFolder ) ) continue;
            foreach( var d in Directory.GetDirectories( prefixFolder ).Select( p => new NormalizedPath( p ) )
                                       .OrderBy( p => p.LastPart, StringComparer.OrdinalIgnoreCase ) )
            {
                var name = d.LastPart;
                if( !File.Exists( d.AppendPart( '$' + name ).AppendPart( FileStore.LocalStoreName ).AppendPart( InstanceFileName ) ) ) continue;
                var u = await TryLoadTenantAsync( monitor, template, name ).ConfigureAwait( false );
                if( u != null )
                {
                    monitor.Warn( $"The run-time tenant {u} is not loaded: it is kept (see IApplicationIdentityService.UnloadedTenants)." );
                    unloaded.Add( u );
                }
            }
        }
        _service._unloadedTenants = unloaded.ToArray();
    }

    async Task<UnloadedTenant?> TryLoadTenantAsync( IActivityMonitor monitor, TenantDomainTemplate template, string name )
    {
        if( !ConfigurationTemplate.IsValidInstanceName( name ) )
        {
            return new UnloadedTenant( template, name, null, UnloadedTenantReason.InvalidRecord, $"'{name}' is not a valid instance name." );
        }
        var fullName = template.GetFullName( name );
        var local = OpenLocalStore( fullName );
        var instances = template.Instances;
        if( instances.IsConfiguredName( name ) )
        {
            // The configuration names it: the configured tenant domain is this party (the newer decision). Its record is
            // stale: it is dropped.
            monitor.Warn( $"'{fullName}' is a configured instance of the tenant template {template}: its run-time record is dropped." );
            local.TryTrash( monitor, RecordPath( local ) );
            return null;
        }
        if( !TryReadRecord( local, out var index ) )
        {
            return new UnloadedTenant( template, name, null, UnloadedTenantReason.InvalidRecord, $"Its record '{RecordPath( local )}' can't be read." );
        }
        if( index > instances.MaxInstanceCount )
        {
            return new UnloadedTenant( template, name, index, UnloadedTenantReason.IndexAboveBound,
                                       $"Its index {index} is above the \"{ConfigurationTemplate.MaxInstanceCountKey}\" ({instances.MaxInstanceCount}) of its template." );
        }
        if( index <= instances.Count )
        {
            return new UnloadedTenant( template, name, index, UnloadedTenantReason.IndexTaken,
                                       $"Its index {index} is the one of the configured instance '{instances[index - 1].Name}'." );
        }
        if( LoadedInstances( template ).FirstOrDefault( d => d.Configuration.Instance!.Index == index ) is { } other )
        {
            return new UnloadedTenant( template, name, index, UnloadedTenantReason.IndexTaken, $"Its index {index} is the one of '{other.FullName}'." );
        }
        using( var l = local.TryAcquireLock( LocalParty.PartyLockName ) )
        {
            if( l == null )
            {
                return new UnloadedTenant( template, name, index, UnloadedTenantReason.HeldByAnotherProcess, "It runs in another process." );
            }
        }
        using( monitor.OpenInfo( $"Creating the run-time tenant '{fullName}' (#{index}) of the template {template}." ) )
        {
            if( await CreateTenantInstanceAsync( monitor, template, instances.CreateDynamic( index, name ), writeRecord: false ).ConfigureAwait( false ) == null )
            {
                monitor.CloseGroup( "Failed." );
                return new UnloadedTenant( template, name, index, UnloadedTenantReason.CreationFailed, "Its creation failed (see the logs)." );
            }
        }
        return null;
    }

    // IApplicationIdentityService.AddTenantInstanceAsync, in the loop.
    internal async Task<TenantDomainParty?> CreateNewTenantAsync( IActivityMonitor monitor, TenantDomainTemplate template, string name )
    {
        var fullName = template.GetFullName( name );
        using( monitor.OpenInfo( $"Creating the run-time tenant '{fullName}' of the template {template}." ) )
        {
            if( File.Exists( RecordPath( OpenLocalStore( fullName ) ) ) )
            {
                monitor.Error( $"A run-time tenant '{fullName}' exists (loaded or not): its name can't be used." );
                monitor.CloseGroup( "Failed." );
                return null;
            }
            var index = FindFreeIndex( template );
            if( index == 0 )
            {
                monitor.Error( $"The {template.Instances.MaxInstanceCount} instances of the template {template} exist (its \"{ConfigurationTemplate.MaxInstanceCountKey}\")." );
                monitor.CloseGroup( "Failed." );
                return null;
            }
            var t = await CreateTenantInstanceAsync( monitor, template, template.Instances.CreateDynamic( index, name ), writeRecord: true ).ConfigureAwait( false );
            monitor.CloseGroup( t != null ? $"Created with the index {index}." : "Failed." );
            return t;
        }
    }

    // IApplicationIdentityService.SetTenantIndexAsync, in the loop: the tenant domain is destroyed (or unloaded).
    internal async Task<bool> MoveTenantAsync( IActivityMonitor monitor, TenantDomainTemplate template, string name, int index, int? previousLoadedIndex )
    {
        var fullName = template.GetFullName( name );
        using( monitor.OpenInfo( $"Moving the run-time tenant '{fullName}' to the index {index}." ) )
        {
            if( LoadedInstances( template ).FirstOrDefault( d => d.Configuration.Instance!.Index == index ) is { } other )
            {
                monitor.Error( $"The index {index} is the one of '{other.FullName}'." );
                await RestoreAsync( previousLoadedIndex ).ConfigureAwait( false );
                monitor.CloseGroup( "Failed." );
                return false;
            }
            var local = OpenLocalStore( fullName );
            bool hasPrevious = TryReadRecord( local, out var recorded );
            using( var l = local.TryAcquireLock( LocalParty.PartyLockName ) )
            {
                if( l == null )
                {
                    monitor.Error( $"'{fullName}' runs in another process." );
                    monitor.CloseGroup( "Failed." );
                    return false;
                }
                if( !TryWriteRecord( monitor, local, index ) )
                {
                    await RestoreAsync( previousLoadedIndex ).ConfigureAwait( false );
                    monitor.CloseGroup( "Failed." );
                    return false;
                }
            }
            var t = await CreateTenantInstanceAsync( monitor, template, template.Instances.CreateDynamic( index, name ), writeRecord: false ).ConfigureAwait( false );
            if( t == null )
            {
                // The record is restored: the tenant is where it was.
                if( hasPrevious ) TryWriteRecord( monitor, local, recorded );
                await RestoreAsync( previousLoadedIndex ).ConfigureAwait( false );
                monitor.CloseGroup( "Failed." );
                return false;
            }
            await AddPinnedTemplateInstancesAsync( monitor, new[] { t } ).ConfigureAwait( false );
            RemoveUnloaded( fullName );
            return true;
        }

        async Task RestoreAsync( int? previous )
        {
            if( previous.HasValue
                && await CreateTenantInstanceAsync( monitor, template, template.Instances.CreateDynamic( previous.Value, name ), writeRecord: false ).ConfigureAwait( false ) is { } t )
            {
                await AddPinnedTemplateInstancesAsync( monitor, new[] { t } ).ConfigureAwait( false );
            }
        }
    }

    // IApplicationIdentityService.RemoveTenantInstanceAsync, in the loop: the tenant domain is destroyed (or unloaded).
    internal async Task<bool> ForgetTenantAsync( IActivityMonitor monitor, TenantDomainTemplate template, string name )
    {
        var fullName = template.GetFullName( name );
        using( monitor.OpenInfo( $"Removing the run-time tenant '{fullName}'." ) )
        {
            var local = OpenLocalStore( fullName );
            using var l = local.TryAcquireLock( LocalParty.PartyLockName );
            if( l == null )
            {
                monitor.Error( $"'{fullName}' runs in another process: it can't be removed here." );
                monitor.CloseGroup( "Failed." );
                return false;
            }
            // The pins of its run-time remote instances: their folders are under its domain, a new tenant of this name
            // would bring them back. Any run-time index instantiates its remote templates (their names don't use its index).
            var instances = template.Instances;
            int probeIndex = TryReadRecord( local, out var recorded ) && instances.IsDynamicIndex( recorded ) ? recorded : instances.Count + 1;
            if( instances.IsDynamicIndex( probeIndex )
                && template.CreateInstance( monitor, instances.CreateDynamic( probeIndex, name ) ) is { } c )
            {
                foreach( var rt in c.RemoteTemplates )
                {
                    foreach( var (folder, _) in FindPinnedInstanceFolders( monitor, rt ).ToList() )
                    {
                        LocalParty.TrashTrustFiles( monitor, new FileStore( _service.Configuration.StoreFileSystem, folder, createFolder: false ) );
                    }
                }
            }
            // Its keys are deleted by their drivers: a private key never goes to a trash bin.
            bool forgotten = true;
            for( int i = _service._builders.Count - 1; i >= 0; --i )
            {
                var d = _service._builders[i];
                try
                {
                    await d.ForgetLocalPartyAsync( monitor, fullName, local ).ConfigureAwait( false );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While the '{d.FeatureName}' feature forgets '{fullName}'.", ex );
                    forgotten = false;
                }
            }
            if( !forgotten )
            {
                monitor.CloseGroup( "Failed: its private store is not trashed." );
                return false;
            }
            // What remains of its private store is trashed: its record with it (the lock files stay: they are never deleted).
            if( Directory.Exists( local.FolderPath ) )
            {
                foreach( var f in Directory.EnumerateFiles( local.FolderPath, "*", SearchOption.AllDirectories ).Select( p => new NormalizedPath( p ) ).ToList() )
                {
                    if( f.StartsWith( local.TrashBinPath )
                        || f.LastPart.StartsWith( "$Lock.", StringComparison.OrdinalIgnoreCase )
                        || f.LastPart.StartsWith( StoreFileSystem.TempFilePrefix, StringComparison.OrdinalIgnoreCase ) )
                    {
                        continue;
                    }
                    local.TryTrash( monitor, f );
                }
            }
            RemoveUnloaded( fullName );
            return true;
        }
    }

    void RemoveUnloaded( NormalizedPath fullName )
    {
        _service._unloadedTenants = _service._unloadedTenants.Where( u => !u.FullName.Path.Equals( fullName.Path, StringComparison.OrdinalIgnoreCase ) ).ToArray();
    }
}
