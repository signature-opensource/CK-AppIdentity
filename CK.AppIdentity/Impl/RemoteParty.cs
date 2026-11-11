using CK.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    // Set when the persisted description could not be read: the next announcement rewrites it.
    volatile bool _invalidPersistedDescription;

    internal RemoteParty( RemotePartyConfiguration configuration, LocalParty owner, bool isDynamic )
        : base( configuration, isDynamic, owner.ApplicationIdentityService )
    {
        _owner = owner;
        _isDynamic = isDynamic;
        if( isDynamic ) _destroyTCS = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
    }

    public new RemotePartyConfiguration Configuration => Unsafe.As<RemotePartyConfiguration>( _configuration );

    /// <summary>
    /// The file of the announced <see cref="IParty.Description"/>, in the remote's folder of the shared store.
    /// </summary>
    internal const string DescriptionFileName = "PartyDescription.json";

    public bool SetDescription( IActivityMonitor monitor, PartyDescription description )
    {
        Throw.CheckNotNullArgument( monitor );
        var previous = DoSetDescription( description );
        if( previous == null ) return false;
        ApplicationIdentityService.CheckRelayedRoles( monitor, this, isAnnouncement: true );
        if( previous.PublicUrl != null && !Equals( previous.PublicUrl, description.PublicUrl ) )
        {
            monitor.Warn( $"The public URL of '{FullName}' changed from '{previous.PublicUrl}' to '{description.PublicUrl?.ToString() ?? "(none)"}'." );
        }
        else
        {
            monitor.Info( $"Description of '{FullName}': {description}." );
        }
        // Only the persisted projection is written (see PartyDescription.Write): a change of the routes, the collected
        // items or the encryption key alone doesn't rewrite the file (this runs on the caller's thread, often a channel's).
        // An unreadable file is rewritten by the first announcement.
        if( _invalidPersistedDescription || !HasSamePersistedContent( previous, description ) )
        {
            _invalidPersistedDescription = false;
            PersistDescription( monitor, description );
        }
        // The agent's loop raises ILocalParty.RemoteDescriptionChanged: this stays synchronous.
        ApplicationIdentityService.Agent.OnRemoteDescriptionChanged( this );
        return true;
    }

    static bool HasSamePersistedContent( PartyDescription a, PartyDescription b )
        => Equals( a.PublicUrl, b.PublicUrl )
           && a.ClientApplications.SequenceEqual( b.ClientApplications )
           && a.Roles.SequenceEqual( b.Roles )
           && a.Data.SequenceEqual( b.Data );

    void PersistDescription( IActivityMonitor monitor, PartyDescription description )
    {
        try
        {
            SharedFileStore.WriteAtomically( SharedFileStore.FolderPath.AppendPart( DescriptionFileName ), stream =>
            {
                using var w = new Utf8JsonWriter( stream, new JsonWriterOptions { Indented = true } );
                description.Write( w );
            } );
        }
        catch( Exception ex )
        {
            monitor.Warn( $"While persisting the description of '{FullName}'.", ex );
        }
    }

    /// <summary>
    /// Reads the last persisted description, if any (when the party is set up).
    /// </summary>
    internal void LoadDescription( IActivityMonitor monitor )
    {
        var path = SharedFileStore.FolderPath.AppendPart( DescriptionFileName );
        try
        {
            using var stream = SharedFileStore.OpenReadStream( path );
            using var doc = JsonDocument.Parse( stream );
            if( PartyDescription.TryRead( doc.RootElement, out var d, out var error ) )
            {
                DoSetDescription( d );
                monitor.Trace( $"Last known description of '{FullName}': {d}." );
                ApplicationIdentityService.CheckRelayedRoles( monitor, this, isAnnouncement: false );
            }
            else
            {
                monitor.Warn( $"Invalid '{path}' ignored: {error}" );
                _invalidPersistedDescription = true;
            }
        }
        catch( Exception ex ) when( ex is FileNotFoundException or DirectoryNotFoundException )
        {
            // Never announced.
        }
        catch( Exception ex )
        {
            monitor.Warn( $"While reading '{path}' (ignored).", ex );
            _invalidPersistedDescription = true;
        }
    }

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
