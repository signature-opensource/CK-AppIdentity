using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class StaticFeatureTests
{
    static readonly List<string> _log = new List<string>();
    static string? _failingStart;
    static Func<ApplicationIdentityService, IActivityMonitor, Task>? _onStarted;

    static void Log( string s )
    {
        lock( _log ) _log.Add( s );
    }

    [SetUp]
    public void Reset()
    {
        _log.Clear();
        _failingStart = null;
        _onStarted = null;
    }

    [CKTypeDefiner]
    public abstract class RecordingStaticFeatureDriver : ApplicationIdentityStaticFeatureDriver
    {
        protected RecordingStaticFeatureDriver( ApplicationIdentityService s, bool isAllowedByDefault = false )
            : base( s, isAllowedByDefault )
        {
        }

        protected RecordingStaticFeatureDriver( ApplicationIdentityService s, string featureName )
            : base( s, featureName, false )
        {
        }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context )
        {
            Log( $"Setup:{FeatureName}" );
            return Task.FromResult( true );
        }

        protected override Task<bool> SetupDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
        {
            Log( $"SetupDynamicFeatureRemote:{FeatureName}:{party.PartyName}" );
            return Task.FromResult( true );
        }

        protected override Task TeardownDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
        {
            Log( $"TeardownDynamicFeatureRemote:{FeatureName}:{party.PartyName}" );
            return Task.CompletedTask;
        }

        protected override Task TeardownAsync( FeatureLifetimeContext context )
        {
            Log( $"Teardown:{FeatureName}" );
            return Task.CompletedTask;
        }

        protected override async Task OnHostStartedAsync( IActivityMonitor monitor, CancellationToken cancel )
        {
            if( _onStarted != null ) await _onStarted( ApplicationIdentityService, monitor );
            if( _failingStart == FeatureName ) throw new CKException( $"{FeatureName} failed to start." );
            Log( $"Started:{FeatureName}" );
        }

        protected override Task OnHostStoppingAsync( IActivityMonitor monitor, CancellationToken cancel )
        {
            Log( $"Stopping:{FeatureName}" );
            return Task.CompletedTask;
        }
    }

    public class SAFeatureDriver : RecordingStaticFeatureDriver
    {
        public SAFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    // Depends on SA: comes after it.
    public class SBFeatureDriver : RecordingStaticFeatureDriver
    {
        public SBFeatureDriver( ApplicationIdentityService s, SAFeatureDriver a ) : base( s ) { }
    }

    // Allowed by default.
    public class SDFeatureDriver : RecordingStaticFeatureDriver
    {
        public SDFeatureDriver( ApplicationIdentityService s ) : base( s, isAllowedByDefault: true ) { }
    }

    // The Local side of the "SC" asymmetric feature: the remotes that use it ("SCRemote") can be dynamic.
    public class LocalSCFeatureDriver : RecordingStaticFeatureDriver
    {
        public LocalSCFeatureDriver( ApplicationIdentityService s ) : base( s, "SC" ) { }
    }

    static IHost BuildHost( Action<MutableConfigurationSection> configure, params Type[] drivers )
    {
        var config = new MutableConfigurationSection( "DontCare" );
        var ckApp = config.GetMutableSection( "CK-AppIdentity" );
        ckApp["FullName"] = "D/$App/#Dev";
        configure( ckApp );
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.Sources.Add( new ChainedConfigurationSource { Configuration = config } );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        foreach( var t in drivers )
        {
            builder.Services.AddSingleton( t );
            builder.Services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( t ) );
        }
        return builder.AddApplicationIdentityServiceConfiguration().CKBuild();
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task static_features_start_after_the_initialization_and_stop_before_the_teardown_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Local:AllowFeatures"] = "SA, SB", typeof( SBFeatureDriver ), typeof( SAFeatureDriver ) );
        await host.StartAsync( cancel );
        _log.ShouldBe( ["Setup:SA", "Setup:SB", "Started:SA", "Started:SB"] );
        await host.StopAsync( cancel );
        _log.ShouldBe( ["Setup:SA", "Setup:SB", "Started:SA", "Started:SB",
                        "Stopping:SB", "Stopping:SA", "Teardown:SB", "Teardown:SA"] );
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task a_static_feature_start_runs_outside_the_agent_loop_Async( CancellationToken cancel )
    {
        // Adding a party is a job of the agent: from the agent's loop, this would never complete.
        IRemoteParty? added = null;
        _onStarted = async ( s, monitor ) => added ??= await s.AddRemoteAsync( monitor, c => c["PartyName"] = "Dyn" );
        using var host = BuildHost( c => c["Local:AllowFeatures"] = "SA", typeof( SAFeatureDriver ) );
        await host.StartAsync( cancel );
        added.ShouldNotBeNull().IsDynamic.ShouldBeTrue();
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task a_failing_start_fails_the_host_start_and_stops_the_started_features_Async( CancellationToken cancel )
    {
        _failingStart = "SB";
        using var host = BuildHost( c => c["Local:AllowFeatures"] = "SA, SB", typeof( SAFeatureDriver ), typeof( SBFeatureDriver ) );
        await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
        _log.ShouldBe( ["Setup:SA", "Setup:SB", "Started:SA", "Stopping:SA"] );
        await host.StopAsync( cancel );
        // The failed start stopped SA: it is not stopped twice.
        _log.ShouldBe( ["Setup:SA", "Setup:SB", "Started:SA", "Stopping:SA", "Teardown:SB", "Teardown:SA"] );
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task StartAndInitializeAsync_and_DisposeAsync_start_and_stop_once_Async( CancellationToken cancel )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "D/$App/#Dev";
            c["Local:AllowFeatures"] = "SA";
        } ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddSingleton( c );
        services.AddSingleton<ApplicationIdentityService>();
        services.AddSingleton<SAFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<SAFeatureDriver>() );
        await using var sp = services.BuildServiceProvider();
        var s = sp.GetRequiredService<ApplicationIdentityService>();
        await s.StartAndInitializeAsync().WaitAsync( cancel );
        await s.StartAndInitializeAsync().WaitAsync( cancel );
        _log.ShouldBe( ["Setup:SA", "Started:SA"] );
        await s.DisposeAsync();
        await s.DisposeAsync();
        _log.ShouldBe( ["Setup:SA", "Started:SA", "Stopping:SA", "Teardown:SA"] );
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task a_dynamic_party_cannot_allow_a_static_feature_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c =>
        {
            c["Local:AllowFeatures"] = "SA";
            // A tenant domain of the initial configuration is static: it can allow SA.
            c["Parties:0:FullName"] = "T/$T";
            c["Parties:0:Local:AllowFeatures"] = "SA";
            c["Parties:0:Parties:0:PartyName"] = "TR";
        }, typeof( SAFeatureDriver ), typeof( SDFeatureDriver ) );
        await host.StartAsync( cancel );
        var s = host.Services.GetRequiredService<ApplicationIdentityService>();
        var sa = host.Services.GetRequiredService<SAFeatureDriver>();
        var sd = host.Services.GetRequiredService<SDFeatureDriver>();
        var tenant = s.TenantDomains.ShouldHaveSingleItem();
        tenant.IsDynamic.ShouldBeFalse();
        sa.IsAllowedFeature( tenant ).ShouldBeTrue();

        // A dynamic remote, a dynamic tenant domain or a remote of a dynamic tenant domain that allows SA fail.
        (await s.AddRemoteAsync( TestHelper.Monitor, c => { c["PartyName"] = "R1"; c["AllowFeatures"] = "SA"; } )).ShouldBeNull();
        (await s.AddTenantDomainAsync( TestHelper.Monitor, c => { c["FullName"] = "D1/$D1"; c["Local:AllowFeatures"] = "SA"; } )).ShouldBeNull();
        (await s.AddTenantDomainAsync( TestHelper.Monitor, c =>
        {
            c["FullName"] = "D2/$D2";
            c["Parties:0:PartyName"] = "R2";
            c["Parties:0:AllowFeatures"] = "SA";
        } )).ShouldBeNull();

        // Dynamic parties are still supported: without SA, they are set up. The default of SD doesn't apply to them.
        var r = (await s.AddRemoteAsync( TestHelper.Monitor, c => c["PartyName"] = "R3" )).ShouldNotBeNull();
        var d = (await s.AddTenantDomainAsync( TestHelper.Monitor, c => c["FullName"] = "D3/$D3" )).ShouldNotBeNull();
        sd.IsAllowedFeature( s ).ShouldBeTrue();
        sd.IsAllowedFeature( tenant ).ShouldBeTrue();
        sd.IsAllowedFeature( r ).ShouldBeFalse();
        sd.IsAllowedFeature( d ).ShouldBeFalse();
        sa.IsAllowedFeature( r ).ShouldBeFalse();
        await r.DestroyAsync();
        await d.DestroyAsync();
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task the_dynamic_remotes_of_a_static_feature_are_supported_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Local:AllowFeatures"] = "SC", typeof( LocalSCFeatureDriver ) );
        await host.StartAsync( cancel );
        var s = host.Services.GetRequiredService<ApplicationIdentityService>();
        var sc = host.Services.GetRequiredService<LocalSCFeatureDriver>();
        sc.IsAllowedFeature( s ).ShouldBeTrue();

        // A dynamic remote that is a remote of the feature: allowed, set up by SetupDynamicFeatureRemoteAsync.
        var r = (await s.AddRemoteAsync( TestHelper.Monitor, c => { c["PartyName"] = "R"; c["AllowFeatures"] = "SCRemote"; } )).ShouldNotBeNull();
        sc.IsAllowedFeature( r ).ShouldBeTrue();
        await r.DestroyAsync();
        _log.ShouldBe( ["Setup:SC", "Started:SC", "SetupDynamicFeatureRemote:SC:$R", "TeardownDynamicFeatureRemote:SC:$R"] );

        // A dynamic tenant domain that would be the feature fails.
        (await s.AddTenantDomainAsync( TestHelper.Monitor, c => { c["FullName"] = "D1/$D1"; c["Local:AllowFeatures"] = "SC"; } )).ShouldBeNull();
        // A dynamic remote that would be the feature (its local party is not the "SC") fails.
        (await s.AddTenantDomainAsync( TestHelper.Monitor, c => { c["FullName"] = "D2/$D2"; c["Parties:0:PartyName"] = "X"; c["Parties:0:AllowFeatures"] = "SCRemote"; } )).ShouldBeNull();
        await host.StopAsync( cancel );
    }
}
