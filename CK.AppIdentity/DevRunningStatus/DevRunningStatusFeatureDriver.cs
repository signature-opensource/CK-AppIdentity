using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// The "DevRunningStatus" static feature: when the application runs in the "#Dev" environment with the "#Dev/Store" of
/// its development solution (<see cref="ApplicationIdentityServiceConfiguration.IsDevStore"/>), it writes a <see cref="FileName"/> file in the root party's store folder
/// (<see cref="IParty.SharedFileStore"/>) while the host runs: the launcher of the solution reads it to know when the
/// party is ready and where it is. The file is assembled from the <see cref="IDevRunningStatusContributor"/>.
/// <para>
/// Otherwise (production), this feature is dormant: its setup checks once and stops there (no file, no subscription, no
/// timer, the contributors are never activated). It is allowed by default: "DisallowFeatures" can switch it off.
/// </para>
/// </summary>
public sealed class DevRunningStatusFeatureDriver : ApplicationIdentityStaticFeatureDriver
{
    /// <summary>
    /// The name of the file in the store folder of the root party.
    /// </summary>
    public const string FileName = "DevRunningStatus.json";

    // The rewrites of a change are batched.
    const int _rewriteDelay = 200;

    readonly IReadOnlyList<IDevRunningStatusContributor> _contributors;
    readonly IServiceProvider _services;
    readonly object _lock;
    NormalizedPath _path;
    bool _active;
    bool _started;
    bool _stopping;
    Task? _pendingRewrite;

    /// <summary>
    /// Initializes a new <see cref="DevRunningStatusFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="contributors">The contributors.</param>
    /// <param name="services">The services of the host.</param>
    public DevRunningStatusFeatureDriver( ApplicationIdentityService s, IEnumerable<IDevRunningStatusContributor> contributors, IServiceProvider services )
        : base( s, isAllowedByDefault: true )
    {
        _contributors = contributors.ToArray();
        _services = services;
        _lock = new object();
    }

    /// <summary>
    /// Gets whether this feature writes the file: the root party allows it, its environment is "#Dev" and its store is
    /// the "#Dev/Store" of the solution (<see cref="ApplicationIdentityServiceConfiguration.IsDevStore"/>).
    /// </summary>
    public bool IsActive => _active;

    /// <summary>
    /// Gets the path of the file (empty when not <see cref="IsActive"/>).
    /// </summary>
    public NormalizedPath FilePath => _path;

    /// <inheritdoc />
    protected internal override Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        var s = ApplicationIdentityService;
        _active = IsRootAllowed
                  && s.EnvironmentName == CoreApplicationIdentity.DefaultEnvironmentName
                  && s.Configuration.IsDevStore;
        if( _active )
        {
            _path = s.SharedFileStore.FolderPath.AppendPart( FileName );
            context.Monitor.Info( $"The running status of '{s.FullName}' is written to '{_path}'." );
        }
        return Task.FromResult( true );
    }

    /// <inheritdoc />
    protected internal override Task OnHostStartedAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        if( !_active ) return Task.CompletedTask;
        foreach( var c in _contributors )
        {
            c.Activate( monitor, OnChanged );
        }
        lock( _lock )
        {
            _started = true;
            Write();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected internal override async Task OnHostStoppingAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        if( !_active ) return;
        Task? pending;
        lock( _lock )
        {
            _stopping = true;
            pending = _pendingRewrite;
        }
        if( pending != null ) await pending.ConfigureAwait( false );
        ApplicationIdentityService.SharedFileStore.TryTrash( monitor, _path, immediateDelete: true );
    }

    /// <inheritdoc />
    protected override Task<bool> SetupDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        // A dynamic remote appears in the remotes.
        if( _active ) OnChanged();
        return Task.FromResult( true );
    }

    /// <inheritdoc />
    protected override Task TeardownDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        if( _active ) OnChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected internal override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;

    void OnChanged()
    {
        lock( _lock )
        {
            if( !_started || _stopping || _pendingRewrite != null ) return;
            _pendingRewrite = RewriteAsync();
        }

        async Task RewriteAsync()
        {
            await Task.Delay( _rewriteDelay ).ConfigureAwait( false );
            lock( _lock )
            {
                _pendingRewrite = null;
                if( !_stopping ) Write();
            }
        }
    }

    // Called in the lock.
    void Write()
    {
        try
        {
            ApplicationIdentityService.SharedFileStore.WriteAtomically( _path, stream =>
            {
                using var w = new Utf8JsonWriter( stream, new JsonWriterOptions { Indented = true } );
                w.WriteStartObject();
                WriteCore( w );
                foreach( var c in _contributors ) c.Write( w );
                w.WriteEndObject();
            } );
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Warn( $"While writing '{_path}'.", ex );
        }
    }

    void WriteCore( Utf8JsonWriter w )
    {
        var s = ApplicationIdentityService;
        using var process = Process.GetCurrentProcess();
        w.WriteString( "FullName", s.FullName.Path );
        w.WriteNumber( "ProcessId", process.Id );
        w.WriteString( "ProcessStartTime", process.StartTime.ToUniversalTime() );
        var contentRoot = _services.GetService<IHostEnvironment>()?.ContentRootPath;
        if( contentRoot != null ) w.WriteString( "ContentRootPath", contentRoot.Replace( '\\', '/' ) );
        w.WriteBoolean( "Ready", _contributors.All( c => c.IsReady ) );
        w.WriteStartArray( "Remotes" );
        foreach( var r in s.AllRemotes )
        {
            w.WriteStartObject();
            w.WriteString( "FullName", r.FullName.Path );
            w.WriteBoolean( "Outgoing", r.Address != null );
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
