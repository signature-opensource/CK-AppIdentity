using CK.Core;
using CK.Monitoring;
using CK.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Testing.Platform.Configurations;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Core.ActivityMonitor;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;


[TestFixture]
public class FeatureBuilderInitializationTests
{
    [Test]
    public async Task without_feature_builders_Async()
    {
        var config = new MutableConfigurationSection( "DontCare" );
        var ckApp = config.GetMutableSection( "CK-AppIdentity" );
        ckApp["DomainName"] = "D";
        ckApp["EnvironmentName"] = "#Production";
        ckApp["PartyName"] = "MyApp";
        ckApp["Parties:0:PartyName"] = "Remote1";
        ckApp["Parties:1:PartyName"] = "Remote2";
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.Sources.Add( new ChainedConfigurationSource { Configuration = config } );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );

        using var app = builder.AddApplicationIdentityServiceConfiguration()
                               .CKBuild();

        await app.StartAsync();
        var s = app.Services.GetRequiredService<ApplicationIdentityService>();

        s.DomainName.ShouldBe( "D" );
        s.EnvironmentName.ShouldBe( "#Production" );
        s.PartyName.ShouldBe( "$MyApp" );
        s.Features.ShouldBeEmpty();

        s.Parties.Count().ShouldBe( 2 );
        var r1 = s.Remotes.Single( r => r.PartyName == "$Remote1" );
        r1.IsDynamic.ShouldBeFalse();
        r1.Address.ShouldBeNull();
        r1.DomainName.ShouldBe( "D" );
        r1.EnvironmentName.ShouldBe( "#Production" );
        r1.Features.ShouldBeEmpty();

        var r2 = s.Remotes.Single( r => r.PartyName == "$Remote2" );
        r2.IsDynamic.ShouldBeFalse();
        r2.Address.ShouldBeNull();
        r2.DomainName.ShouldBe( "D" );
        r2.EnvironmentName.ShouldBe( "#Production" );
        r2.Features.ShouldBeEmpty();

    }

    [CKTypeDefiner]
    [PartyFeature( "CheckOrder", TypeActivation.ByDefault )]
    public abstract class CheckOrderFeatureDriver : ApplicationIdentityFeatureDriver
    {
        internal static int _count;
        internal static int _currentSetupOrder;
        internal static int _dynamicSetupCount;
        internal static int _dynamicTeardownCount;
        internal static int _teardownCount;

        public static void Reset()
        {
            _count = 0;
            _currentSetupOrder = 0;
            _dynamicSetupCount = 0;
            _dynamicTeardownCount = 0;
            _teardownCount = 0;
        }

        protected CheckOrderFeatureDriver( ApplicationIdentityService s )
            : base( s )
        {
            ++_count;
        }

        public int SetupOrder { get; private set; }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context )
        {
            SetupOrder = _currentSetupOrder++;
            context.Monitor.Trace( $"Setup {GetType().Name} ({SetupOrder})." );
            return Task.FromResult( true );
        }

        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty remote )
        {
            _dynamicSetupCount++;
            context.Monitor.Trace( $"SetupDynamic {GetType().Name} ({SetupOrder})." );
            context.Memory.GetValueOrDefault( "DynamicSetupOrder", 0 ).ShouldBe( SetupOrder );
            context.Memory["DynamicSetupOrder"] = SetupOrder + 1;
            return Task.FromResult( true );
        }

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty remote )
        {
            _dynamicTeardownCount++;
            context.Monitor.Trace( $"TeardownDynamic {GetType().Name} ({SetupOrder})." );
            int revertOrder = _currentSetupOrder - SetupOrder - 1;
            context.Memory.GetValueOrDefault( "DynamicTeardownOrder", 0 ).ShouldBe( revertOrder );
            // Next expected value.
            context.Memory["DynamicTeardownOrder"] = revertOrder + 1;
            return Task.CompletedTask;
        }

        protected override Task TeardownAsync( FeatureLifetimeContext context )
        {
            _teardownCount++;
            context.Monitor.Trace( $"Teardown {GetType().Name} ({SetupOrder})." );
            int revertOrder = _currentSetupOrder - SetupOrder - 1;
            context.Memory.GetValueOrDefault( "TeardownOrder", 0 ).ShouldBe( revertOrder );
            // Next expected value.
            context.Memory["TeardownOrder"] = revertOrder + 1;
            return Task.CompletedTask;
        }
    }

    [PartyFeature( "F1", TypeActivation.Optional )]
    public class F1FeatureDriver : CheckOrderFeatureDriver
    {
        public F1FeatureDriver( ApplicationIdentityService s ) : base( s )
        {
        }
    }

    [PartyFeature( "F2x1", TypeActivation.Optional )]
    public class F2x1FeatureDriver : CheckOrderFeatureDriver
    {
        public F2x1FeatureDriver( ApplicationIdentityService s, F1FeatureDriver s1 ) : base( s )
        {
        }
    }

    [PartyFeature( "F3x2", TypeActivation.Optional )]
    public class F3x2FeatureDriver : CheckOrderFeatureDriver
    {
        public F3x2FeatureDriver( ApplicationIdentityService s, F2x1FeatureDriver s2 ) : base( s )
        {
        }
    }

    [PartyFeature( "FAx1", TypeActivation.Optional )]
    public class FAx1FeatureDriver : CheckOrderFeatureDriver
    {
        public FAx1FeatureDriver( ApplicationIdentityService s, F1FeatureDriver s1 ) : base( s )
        {
        }
    }

    [PartyFeature( "FBxA", TypeActivation.Optional )]
    public class FBxAFeatureDriver : CheckOrderFeatureDriver
    {
        public FBxAFeatureDriver( ApplicationIdentityService s, FAx1FeatureDriver sa ) : base( s )
        {
        }
    }

    [PartyFeature( "FCxAx3", TypeActivation.Optional )]
    public class FCxAx3FeatureDriver : CheckOrderFeatureDriver
    {
        public FCxAx3FeatureDriver( ApplicationIdentityService s, FAx1FeatureDriver sa, F3x2FeatureDriver f1 ) : base( s )
        {
        }
    }

    [PartyFeature( "FDxBx2", TypeActivation.Optional )]
    public class FDxBx2FeatureDriver : CheckOrderFeatureDriver
    {
        public FDxBx2FeatureDriver( ApplicationIdentityService s, FBxAFeatureDriver sb, F2x1FeatureDriver f2 ) : base( s )
        {
        }
    }

    [TestCase( true )]
    [TestCase( false )]
    public async Task feature_builders_initialization_follows_the_dependency_order_Async( bool revert )
    {
        CheckOrderFeatureDriver.Reset();
        var builderTypes = new List<Type>() { typeof( F1FeatureDriver ),
                                              typeof( F2x1FeatureDriver ),
                                              typeof( F3x2FeatureDriver ),
                                              typeof( FAx1FeatureDriver ),
                                              typeof( FBxAFeatureDriver ),
                                              typeof( FCxAx3FeatureDriver ),
                                              typeof( FDxBx2FeatureDriver ) };
        if( revert ) builderTypes.Reverse();



        var config = new MutableConfigurationSection( "DontCare" );
        config["FullName"] = "FakeDomain/$FakeApp";
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.Sources.Add( new ChainedConfigurationSource { Configuration = config } );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        foreach( var t in builderTypes )
        {
            builder.Services.AddSingleton( t );
            builder.Services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( t ) );
        }
        builder.Services.AddSingleton( AppIdentityTestHelper.CreateComponentDirectory( builderTypes ) );

        using var app = builder.AddApplicationIdentityServiceConfiguration()
                               .CKBuild();

        await app.StartAsync();
        var identityService = app.Services.GetRequiredService<ApplicationIdentityService>();

        var f1 = app.Services.GetRequiredService<F1FeatureDriver>();
        var f2_1 = app.Services.GetRequiredService<F2x1FeatureDriver>();
        var f3_2 = app.Services.GetRequiredService<F3x2FeatureDriver>();
        var fA_1 = app.Services.GetRequiredService<FAx1FeatureDriver>();
        var fB_A = app.Services.GetRequiredService<FBxAFeatureDriver>();
        var fC_A_3 = app.Services.GetRequiredService<FCxAx3FeatureDriver>();
        var fD_B_2 = app.Services.GetRequiredService<FDxBx2FeatureDriver>();
        CheckOrderFeatureDriver._count.ShouldBe( 7 );

        f1.FeatureName.ShouldBe( "F1" );
        f2_1.FeatureName.ShouldBe( "F2x1" );
        f3_2.FeatureName.ShouldBe( "F3x2" );
        fA_1.FeatureName.ShouldBe( "FAx1" );
        fB_A.FeatureName.ShouldBe( "FBxA" );
        fC_A_3.FeatureName.ShouldBe( "FCxAx3" );
        fD_B_2.FeatureName.ShouldBe( "FDxBx2" );

        f1.SetupOrder.ShouldBe( 0 );
        f2_1.SetupOrder.ShouldBeGreaterThan( f1.SetupOrder );
        f3_2.SetupOrder.ShouldBeGreaterThan( f2_1.SetupOrder );
        fA_1.SetupOrder.ShouldBeGreaterThan( f1.SetupOrder );
        fB_A.SetupOrder.ShouldBeGreaterThan( fA_1.SetupOrder );
        fC_A_3.SetupOrder.ShouldBeGreaterThan( Math.Max( fA_1.SetupOrder, f3_2.SetupOrder ) );
        fD_B_2.SetupOrder.ShouldBeGreaterThan( Math.Max( fB_A.SetupOrder, f2_1.SetupOrder ) );

        var r = await identityService.AddRemoteAsync( TestHelper.Monitor, c =>
        {
            c["PartyName"] = "SomeDynamicRemote";
        } );
        Throw.DebugAssert( r != null );
        CheckOrderFeatureDriver._dynamicSetupCount.ShouldBe( 7 );
        CheckOrderFeatureDriver._dynamicTeardownCount.ShouldBe( 0 );

        await r.DestroyAsync();
        CheckOrderFeatureDriver._dynamicTeardownCount.ShouldBe( 7 );
        CheckOrderFeatureDriver._teardownCount.ShouldBe( 0 );

        await identityService.DisposeAsync();
        CheckOrderFeatureDriver._teardownCount.ShouldBe( 7 );
    }

    [Test]
    [CancelAfter( 1000 )]
    public async Task duplicate_feature_builders_registration_error_detection_Async( CancellationToken cancellation )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "FakeDomain/$FakeApp" ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddSingleton( c );
        services.AddSingleton<ApplicationIdentityService>();
        services.AddSingleton( typeof( F1FeatureDriver ) );
        services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( typeof( F1FeatureDriver ) ) );
        services.AddSingleton( AppIdentityTestHelper.CreateComponentDirectory( typeof( F1FeatureDriver ) ) );
        await Should.NotThrowAsync( () =>
        {
            var sp = services.BuildServiceProvider();
            var s = sp.GetRequiredService<ApplicationIdentityService>();
            _ = ((IHostedService)s).StartAsync( cancellation );
            return s.InitializationTask.WaitAsync( cancellation );
        } );

        services.AddSingleton( typeof( F1FeatureDriver ) );
        services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( typeof( F1FeatureDriver ) ) );

        using( var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 100 ) )
        {
            await Should.ThrowAsync<InvalidOperationException>( () =>
            {
                var sp = services.BuildServiceProvider();
                var s = sp.GetRequiredService<ApplicationIdentityService>();
                _ = ((IHostedService)s).StartAsync( cancellation );
                return s.InitializationTask.WaitAsync( cancellation );
            } );
            ( await WaitForLogAsync( logs, """
                Found duplicated service registration for: 'CK.AppIdentity.Tests.FeatureBuilderInitializationTests.F1FeatureDriver' (2 registrations).
                AppIdentityFeatureBuilder are singletons, they must be registered only once. If you cannot find the duplicate site(s),
                use the (unfortunately costly) TryAddSingleton instead of AddSingleton registration. 
                """ ) ).ShouldBeTrue();
        }
    }


    // The GrandOutput dispatches the logs asynchronously.
    static async Task<bool> WaitForLogAsync( GrandOutputMemoryCollector logs, params string[] texts )
    {
        var all = new List<string>();
        for( int i = 0; i < 50; ++i )
        {
            all.AddRange( logs.ExtractCurrentTexts() );
            if( texts.All( all.Contains ) ) return true;
            await Task.Delay( 20 );
        }
        TestContext.Progress.WriteLine( "COLLECTED:" + Environment.NewLine + string.Join( Environment.NewLine + "---" + Environment.NewLine, all ) );
        return false;
    }

    class Alien : IApplicationIdentityFeatureDriver
    {
        public string FeatureName => "NoWay";

        public bool IsRootAllowed => false;
    }

    [Test]
    [CancelAfter( 1000 )]
    public async Task alien_implementation_feature_builders_registration_error_detection_Async( CancellationToken cancellation )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "FakeDomain/$FakeApp" ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddSingleton( c );
        services.AddSingleton<ApplicationIdentityService>();
        services.AddSingleton( typeof( Alien ) );
        services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( typeof( Alien ) ) );

        using( var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 100 ) )
        {
            await Should.ThrowAsync<InvalidOperationException>( () =>
            {
                var sp = services.BuildServiceProvider();
                var s = sp.GetRequiredService<ApplicationIdentityService>();
                _ = ((IHostedService)s).StartAsync( cancellation );
                return s.InitializationTask.WaitAsync( cancellation );
            } );
            ( await WaitForLogAsync( logs, """
                IAppIdentityFeatureBuilder type 'CK.AppIdentity.Tests.FeatureBuilderInitializationTests.Alien' must inherit from CK.AppIdentity.ApplicationIdentityFeatureDriver base class.
                """ ) ).ShouldBeTrue();
        }

    }

    public abstract class NoopFeatureDriver : ApplicationIdentityFeatureDriver
    {
        protected NoopFeatureDriver( ApplicationIdentityService s )
            : base( s )
        {
        }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );
        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );
        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;
        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    // The "Asym" asymmetric feature: its two sides.
    [PartyFeature( "Asym", TypeActivation.Optional, Side = FeatureSide.Local )]
    public sealed class LocalAsymFeatureDriver : NoopFeatureDriver
    {
        public LocalAsymFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    [PartyFeature( "Asym", TypeActivation.Optional, Side = FeatureSide.Remote )]
    public sealed class RemoteAsymFeatureDriver : NoopFeatureDriver
    {
        public RemoteAsymFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    [PartyFeature( "LocalStorage", TypeActivation.ByDefault )]
    public sealed class LocalStorageFeatureDriver : NoopFeatureDriver
    {
        public LocalStorageFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    static ApplicationIdentityService CreateService( Action<MutableConfigurationSection>? configure = null )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "FakeDomain/$FakeApp";
            configure?.Invoke( c );
        } ).ShouldNotBeNull();
        return new ApplicationIdentityService( c, AppIdentityTestHelper.CreateServiceProvider( typeof( LocalAsymFeatureDriver ),
                                                                                               typeof( RemoteAsymFeatureDriver ),
                                                                                               typeof( LocalStorageFeatureDriver ) ) );
    }

    [Test]
    public void an_asymmetric_feature_is_XXX_on_a_local_party_and_XXXRemote_on_a_remote()
    {
        // The local party is the "Asym": its remotes that declare "AsymRemote" use it.
        var s = CreateService( c =>
        {
            c["Local:Asym"] = "true";
            c["Parties:0:PartyName"] = "AllowsTheRemoteName";
            c["Parties:0:AsymRemote"] = "true";
            c["Parties:1:PartyName"] = "AllowsTheFeatureName";
            c["Parties:1:Asym"] = "true";
        } );
        var local = new LocalAsymFeatureDriver( s );
        var remote = new RemoteAsymFeatureDriver( s );
        var byRemote = s.Remotes.ToDictionary( r => r.PartyName );
        foreach( var d in new ApplicationIdentityFeatureDriver[] { local, remote } )
        {
            d.IsAsymmetric.ShouldBeTrue();
            d.HasLocalSide.ShouldBeTrue();
            d.HasRemoteSide.ShouldBeTrue();
            d.GetFeatureName( s ).ShouldBe( "Asym" );
            d.GetFeatureName( byRemote["$AllowsTheRemoteName"] ).ShouldBe( "AsymRemote" );
            d.IsRootAllowed.ShouldBeTrue();
            d.IsAllowedFeature( byRemote["$AllowsTheRemoteName"] ).ShouldBeTrue();
            d.IsAllowedFeature( byRemote["$AllowsTheFeatureName"] ).ShouldBeFalse();
            d.TryGetFeatureRemote( TestHelper.Monitor, s, out var featureRemote ).ShouldBeTrue();
            featureRemote.ShouldBeNull( "The local party is the feature." );
        }

        // The local party is not the "Asym": the remote that declares "AsymRemote" is the "Asym".
        s = CreateService( c =>
        {
            c["Parties:0:PartyName"] = "TheAsym";
            c["Parties:0:AsymRemote"] = "true";
            c["Parties:1:PartyName"] = "Other";
        } );
        remote = new RemoteAsymFeatureDriver( s );
        remote.HasLocalSide.ShouldBeFalse();
        remote.HasRemoteSide.ShouldBeTrue();
        remote.IsRootAllowed.ShouldBeFalse();
        remote.TryGetFeatureRemote( TestHelper.Monitor, s, out var theAsym ).ShouldBeTrue();
        theAsym.ShouldNotBeNull().PartyName.ShouldBe( "$TheAsym" );

        // Without any "AsymRemote", there is no feature remote.
        s = CreateService( c => c["Parties:0:PartyName"] = "R" );
        remote = new RemoteAsymFeatureDriver( s );
        remote.TryGetFeatureRemote( TestHelper.Monitor, s, out theAsym ).ShouldBeTrue();
        theAsym.ShouldBeNull();

        // A symmetric feature has the same name everywhere.
        s = CreateService( c => c["Parties:0:PartyName"] = "R" );
        var symmetric = new LocalStorageFeatureDriver( s );
        symmetric.IsAsymmetric.ShouldBeFalse();
        symmetric.GetFeatureName( s ).ShouldBe( "LocalStorage" );
        symmetric.GetFeatureName( s.Remotes.Single() ).ShouldBe( "LocalStorage" );
        Should.Throw<InvalidOperationException>( () => symmetric.TryGetFeatureRemote( TestHelper.Monitor, s, out _ ) );
    }

    [Test]
    public void a_local_party_can_use_only_one_feature_remote()
    {
        var s = CreateService( c =>
        {
            c["Defaults:AsymRemote"] = "true";
            c["Parties:0:PartyName"] = "A";
            c["Parties:1:PartyName"] = "B";
        } );
        var remote = new RemoteAsymFeatureDriver( s );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            remote.TryGetFeatureRemote( TestHelper.Monitor, s, out var r ).ShouldBeFalse();
            r.ShouldBeNull();
            texts.ShouldContain( t => t.Contains( "it can use only one remote that is the 'Asym' but the remotes 'FakeDomain/$A/#Dev', 'FakeDomain/$B/#Dev' declare 'AsymRemote'" ) );
        }
    }

    [TestCase( "Local" )]
    [TestCase( "Remote" )]
    [CancelAfter( 2000 )]
    public async Task a_remote_that_declares_XXX_is_an_error_Async( string side, CancellationToken cancellation )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "FakeDomain/$FakeApp";
            // A local party can declare "AsymRemote": it has the Remote side (for the remotes added later).
            c["Local:AsymRemote"] = "true";
            c["Parties:0:PartyName"] = "Other";
            c["Parties:0:Asym"] = "true";
            // Declaring both names is fine (a "Defaults" applies to the local party and to the remotes).
            c["Parties:1:PartyName"] = "Both";
            c["Parties:1:Asym"] = "true";
            c["Parties:1:AsymRemote"] = "true";
        } ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddApplicationIdentityForTests( c, [side == "Local" ? typeof( LocalAsymFeatureDriver ) : typeof( RemoteAsymFeatureDriver )] );

        using( var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 100 ) )
        {
            await Should.ThrowAsync<Exception>( () =>
            {
                var sp = services.BuildServiceProvider();
                var s = sp.GetRequiredService<ApplicationIdentityService>();
                _ = ((IHostedService)s).StartAsync( cancellation );
                return s.InitializationTask.WaitAsync( cancellation );
            } );
            ( await WaitForLogAsync( logs,
                "'FakeDomain/$Other/#Dev' declares the 'Asym' feature: 'Asym' is declared in the \"Local\" of a local party that is the feature. The 'Asym' feature is asymmetric: a remote that is the 'Asym' (or that uses the local 'Asym') declares 'AsymRemote'." ) ).ShouldBeTrue();
        }
    }

    [Test]
    public void the_drivers_of_an_asymmetric_feature_share_its_name()
    {
        var s = CreateService();
        new LocalAsymFeatureDriver( s ).FeatureName.ShouldBe( "Asym" );
        new RemoteAsymFeatureDriver( s ).FeatureName.ShouldBe( "Asym" );
        // Without an explicit feature name, the name is the type name without "FeatureDriver".
        new LocalStorageFeatureDriver( s ).FeatureName.ShouldBe( "LocalStorage" );
    }

    [Test]
    public void the_Local_and_the_Remote_sides_can_be_both_registered()
    {
        var s = CreateService();
        var local = new LocalAsymFeatureDriver( s );
        var remote = new RemoteAsymFeatureDriver( s );
        local.HasRemoteSide.ShouldBeTrue();
        remote.HasLocalSide.ShouldBeTrue();
    }

    // The names and the optionality are the engine's (the PartyComponentDirectory): the rules (an explicit name for the
    // sides of an asymmetric feature, the two sides agree on their optionality, a name is borne by one driver) are
    // checked by the engine (see PartyComponentAnalysisTests in CK.AppIdentity.Engine.Tests).
    [Test]
    public void a_driver_that_is_not_in_the_directory_is_an_error()
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "FakeDomain/$FakeApp" ).ShouldNotBeNull();
        var s = new ApplicationIdentityService( c, AppIdentityTestHelper.CreateServiceProvider( typeof( LocalStorageFeatureDriver ) ) );
        Should.Throw<InvalidOperationException>( () => new LocalAsymFeatureDriver( s ) )
              .Message.ShouldContain( "is not a party component of the application" );
    }

}