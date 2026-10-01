using CK.Core;
using CK.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

/// <summary>
/// The runtime must never hang or leave inconsistent state: throwing subscribers, failing drivers,
/// operations after stop or before start, concurrent destructions.
/// </summary>
[TestFixture]
public class RuntimeRobustnessTests
{
    public sealed class ProbeFeatureDriver : ApplicationIdentityFeatureDriver
    {
        readonly List<string> _log = new();

        public ProbeFeatureDriver( ApplicationIdentityService s ) : base( s, true ) { }

        public string? FailSetupFor { get; set; }

        public string[] Log { get { lock( _log ) return _log.ToArray(); } }

        void Add( string s ) { lock( _log ) _log.Add( s ); }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );

        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
        {
            Add( $"Setup {party.PartyName}" );
            // The folders are created before the setup: the drivers can use the stores.
            if( !Directory.Exists( party.SharedFileStore.FolderPath ) ) Add( $"NoFolder {party.PartyName}" );
            context.Trampoline.OnError( () => Add( $"OnError {party.PartyName}" ) );
            return Task.FromResult( party.PartyName != FailSetupFor );
        }

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
        {
            Add( $"Teardown {party.PartyName}" );
            return Task.CompletedTask;
        }

        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    sealed class FastClock : ApplicationIdentityService.ISystemClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public int HeartBeatPeriod => 20;
    }

    static (ApplicationIdentityService Service, ProbeFeatureDriver Driver) Create( bool fastClock = false )
    {
        var config = ApplicationIdentityServiceConfiguration.CreateEmpty( "Test", "Runtime" );
        var services = new ServiceCollection();
        services.AddSingleton( config );
        services.AddSingleton<ApplicationIdentityService>();
        services.AddSingleton<ProbeFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<ProbeFeatureDriver>() );
        if( fastClock ) services.AddSingleton<ApplicationIdentityService.ISystemClock>( new FastClock() );
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ApplicationIdentityService>(), sp.GetRequiredService<ProbeFeatureDriver>());
    }

    static readonly TimeSpan _timeout = TimeSpan.FromSeconds( 5 );

    [Test]
    public async Task throwing_subscribers_never_prevent_creation_or_destruction_Async()
    {
        var (s, _) = Create();
        await using var _s = s;
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        s.AnyPartyChanged.Sync += ( m, p ) => throw new Exception( "Bad subscriber." );
        s.AnyRemoteChanged.Sync += ( m, p ) => throw new Exception( "Bad subscriber." );

        var r = await s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "R" ).WaitAsync( _timeout );
        r.ShouldNotBeNull( "The remote is created: subscribers' errors are logged." );
        s.Remotes.ShouldContain( r );

        var t = await s.AddTenantDomainAsync( TestHelper.Monitor, c =>
        {
            c["FullName"] = "T/$T";
            c["Parties:0:PartyName"] = "TR";
        } ).WaitAsync( _timeout );
        t.ShouldNotBeNull();

        await r.DestroyAsync().WaitAsync( _timeout );
        await t.DestroyAsync().WaitAsync( _timeout );
        s.Remotes.ShouldNotContain( r );
        s.TenantDomains.ShouldNotContain( t );
    }

    // The shared folder of a remote of the "Test/$Runtime" application.
    static NormalizedPath RemoteFolder( string domainName, string partyName )
    {
        return ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.Combine( $"#Dev/{domainName}/${partyName}" );
    }

    static void DeleteFolder( NormalizedPath path )
    {
        if( Directory.Exists( path ) ) Directory.Delete( path, recursive: true );
    }

    [Test]
    public async Task a_failed_batch_adds_nothing_Async()
    {
        var (s, driver) = Create();
        await using var _s = s;
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        DeleteFolder( RemoteFolder( "Test", "A" ) );
        DeleteFolder( RemoteFolder( "Test", "B" ) );
        driver.FailSetupFor = "$B";

        var added = await s.AddMultipleRemotesAsync( TestHelper.Monitor, c =>
        {
            c["Parties:0:PartyName"] = "A";
            c["Parties:1:PartyName"] = "B";
        } ).WaitAsync( _timeout );

        added.ShouldBeNull();
        s.Remotes.ShouldBeEmpty();
        driver.Log.ShouldBe( ["Setup $A", "Setup $B", "OnError $B", "Teardown $A"],
                             "B undoes its work with OnError, A has been fully set up: it is torn down." );
        Directory.Exists( RemoteFolder( "Test", "A" ) ).ShouldBeFalse( "The folders created for the failed batch are removed." );
        Directory.Exists( RemoteFolder( "Test", "B" ) ).ShouldBeFalse();

        // The names are free: the same batch can be added once the problem is fixed.
        driver.FailSetupFor = null;
        (await s.AddMultipleRemotesAsync( TestHelper.Monitor, c =>
        {
            c["Parties:0:PartyName"] = "A";
            c["Parties:1:PartyName"] = "B";
        } ).WaitAsync( _timeout )).ShouldNotBeNull().Count.ShouldBe( 2 );
    }

    [Test]
    public async Task a_failed_batch_keeps_the_existing_folders_Async()
    {
        var (s, driver) = Create();
        await using var _s = s;
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        // The folder of B exists (another application uses it, or B has been destroyed): it holds its pinned identity.
        var bFolder = RemoteFolder( "Test", "KeptB" );
        Directory.CreateDirectory( bFolder );
        var pinned = bFolder.AppendPart( "Identity.Pinned.public" );
        File.WriteAllText( pinned, "key" );
        DeleteFolder( RemoteFolder( "Test", "KeptA" ) );
        driver.FailSetupFor = "$KeptB";

        (await s.AddMultipleRemotesAsync( TestHelper.Monitor, c =>
        {
            c["Parties:0:PartyName"] = "KeptA";
            c["Parties:1:PartyName"] = "KeptB";
        } ).WaitAsync( _timeout )).ShouldBeNull();

        driver.Log.ShouldNotContain( l => l.StartsWith( "NoFolder" ) );
        File.Exists( pinned ).ShouldBeTrue( "An existing folder is never removed." );
        Directory.Exists( RemoteFolder( "Test", "KeptA" ) ).ShouldBeFalse( "The created one is." );
    }

    [Test]
    public async Task a_dynamic_tenant_creates_its_folders_and_the_ones_of_its_remotes_Async()
    {
        var (s, _) = Create();
        await using var _s = s;
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        DeleteFolder( ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.Combine( "#Dev/FoldersT" ) );
        var t = await s.AddTenantDomainAsync( TestHelper.Monitor, c =>
        {
            c["FullName"] = "FoldersT/$FoldersT";
            c["Parties:0:PartyName"] = "TR";
        } ).WaitAsync( _timeout );
        Throw.DebugAssert( t != null );
        Directory.Exists( t.SharedFileStore.FolderPath ).ShouldBeTrue();
        Directory.Exists( t.LocalFileStore.FolderPath ).ShouldBeTrue( "The local store of a dynamic tenant is created." );
        Directory.Exists( t.Remotes.Single().SharedFileStore.FolderPath ).ShouldBeTrue( "The remotes of a dynamic tenant are created with it." );
    }

    [Test]
    public async Task no_folder_is_created_for_a_rejected_party_Async()
    {
        var (s, _) = Create();
        DeleteFolder( RemoteFolder( "Test", "Rejected" ) );
        // The service is never started: the pending addition is rejected by the agent.
        var pending = s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "Rejected" );
        await s.DisposeAsync();
        (await pending.WaitAsync( _timeout )).ShouldBeNull();
        Directory.Exists( RemoteFolder( "Test", "Rejected" ) ).ShouldBeFalse( "Constructing a dynamic party doesn't create its folders." );
    }

    [Test]
    public async Task operations_after_stop_never_hang_Async()
    {
        var (s, _) = Create();
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        var r = await s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "R" ).WaitAsync( _timeout );
        r.ShouldNotBeNull();

        await s.DisposeAsync().AsTask().WaitAsync( _timeout );

        (await s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "Late" ).WaitAsync( _timeout )).ShouldBeNull();
        await r.DestroyAsync().WaitAsync( _timeout );
        r.IsDestroyed.ShouldBeTrue();
        (await s.StartAndInitializeAsync().WaitAsync( _timeout ).ContinueWith( t => t.Status )).ShouldBe( TaskStatus.RanToCompletion,
            "StartAndInitializeAsync can be called safely after stop." );
    }

    [Test]
    public async Task stopping_before_start_cancels_the_initialization_and_releases_the_pending_operations_Async()
    {
        var (s, _) = Create();
        var pendingAdd = s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "R" );
        await s.DisposeAsync();

        s.InitializationTask.IsCanceled.ShouldBeTrue();
        (await pendingAdd.WaitAsync( _timeout )).ShouldBeNull();
        (await s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "R" ).WaitAsync( _timeout )).ShouldBeNull();
        s.StartAndInitializeAsync().IsCanceled.ShouldBeTrue();
    }

    [Test]
    public async Task concurrent_destruction_of_a_tenant_and_its_remote_Async()
    {
        var (s, driver) = Create();
        await using var _s = s;
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        for( int i = 0; i < 20; ++i )
        {
            var t = await s.AddTenantDomainAsync( TestHelper.Monitor, c =>
            {
                c["FullName"] = $"T{i}/$T{i}";
                c["Parties:0:PartyName"] = "R";
            } ).WaitAsync( _timeout );
            Throw.DebugAssert( t != null );
            var r = t.Remotes.Single();
            int logStart = driver.Log.Length;
            using( var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 200 ) )
            {
                var destroyRemote = Task.Run( r.DestroyAsync );
                var destroyTenant = Task.Run( t.DestroyAsync );
                await Task.WhenAll( destroyRemote, destroyTenant ).WaitAsync( _timeout );
                // Lets the agent log.
                await Task.Delay( 20 );
                logs.ExtractCurrentTexts().ShouldNotContain( text => text.Contains( "Unhandled exception" ) );
            }
            s.TenantDomains.ShouldNotContain( t );
            var iteration = driver.Log.Skip( logStart ).ToList();
            // When the remote is handled first, it is torn down alone. When the tenant is handled first,
            // the remote is torn down with it (drivers use GetAllRemotes) and the remote's job does nothing.
            iteration.Count( l => l == "Teardown $R" ).ShouldBeLessThanOrEqualTo( 1, "The remote is never torn down twice." );
            iteration.Count( l => l == $"Teardown $T{i}" ).ShouldBe( 1, "The tenant is torn down once." );
        }
    }

    [Test]
    public async Task remotes_cannot_be_added_to_a_destroyed_tenant_Async()
    {
        var (s, _) = Create();
        await using var _s = s;
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        var t = await s.AddTenantDomainAsync( TestHelper.Monitor, c => c["FullName"] = "T/$T" ).WaitAsync( _timeout );
        Throw.DebugAssert( t != null );
        t.SetDestroyed();
        (await t.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "R" ).WaitAsync( _timeout )).ShouldBeNull();
        await t.DestroyAsync().WaitAsync( _timeout );
        (await t.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "R" ).WaitAsync( _timeout )).ShouldBeNull();
        s.AllRemotes.ShouldBeEmpty();
    }

    [Test]
    public async Task heartbeats_never_accumulate_Async()
    {
        var (s, _) = Create( fastClock: true );
        await using var _s = s;
        var calls = new List<long>();
        var slowDone = 0L;
        s.Heartbeat.Async += async ( m, count, cancel ) =>
        {
            if( count == 0 )
            {
                // Blocks the agent for 15 periods.
                await Task.Delay( 300, cancel );
                Volatile.Write( ref slowDone, Stopwatch.GetTimestamp() );
            }
            else
            {
                lock( calls ) calls.Add( Stopwatch.GetTimestamp() );
            }
        };
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        await Task.Delay( 600 );
        var done = Volatile.Read( ref slowDone );
        done.ShouldNotBe( 0 );
        int burst;
        lock( calls ) burst = calls.Count( c => Stopwatch.GetElapsedTime( done, c ) < TimeSpan.FromMilliseconds( 15 ) );
        burst.ShouldBeLessThanOrEqualTo( 2, "The 15 missed beats are skipped, not raised in a burst." );
    }

    [Test]
    [CancelAfter( 2000 )]
    public async Task trampoline_stops_on_first_false_Async( CancellationToken cancellation )
    {
        var runner = new BasicTrampolineRunner();
        int secondCalled = 0;
        int onError = 0;
        runner.Trampoline.Add( () => false );
        runner.Trampoline.Add( () => { ++secondCalled; return true; } );
        runner.Trampoline.OnError( () => ++onError );
        await runner.ExecuteAsync( TestHelper.Monitor ).WaitAsync( cancellation );
        runner.Result.ShouldBe( TrampolineResult.Error );
        secondCalled.ShouldBe( 0 );
        onError.ShouldBe( 1 );
    }
}
