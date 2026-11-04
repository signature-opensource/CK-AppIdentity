using CK.Core;
using CK.Monitoring;
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
            : base( s, true )
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

    public class F1FeatureDriver : CheckOrderFeatureDriver
    {
        public F1FeatureDriver( ApplicationIdentityService s ) : base( s )
        {
        }
    }

    public class F2_1FeatureDriver : CheckOrderFeatureDriver
    {
        public F2_1FeatureDriver( ApplicationIdentityService s, F1FeatureDriver s1 ) : base( s )
        {
        }
    }

    public class F3_2FeatureDriver : CheckOrderFeatureDriver
    {
        public F3_2FeatureDriver( ApplicationIdentityService s, F2_1FeatureDriver s2 ) : base( s )
        {
        }
    }

    public class FA_1FeatureDriver : CheckOrderFeatureDriver
    {
        public FA_1FeatureDriver( ApplicationIdentityService s, F1FeatureDriver s1 ) : base( s )
        {
        }
    }

    public class FB_AFeatureDriver : CheckOrderFeatureDriver
    {
        public FB_AFeatureDriver( ApplicationIdentityService s, FA_1FeatureDriver sa ) : base( s )
        {
        }
    }

    public class FC_A_3FeatureDriver : CheckOrderFeatureDriver
    {
        public FC_A_3FeatureDriver( ApplicationIdentityService s, FA_1FeatureDriver sa, F3_2FeatureDriver f1 ) : base( s )
        {
        }
    }

    public class FD_B_2FeatureDriver : CheckOrderFeatureDriver
    {
        public FD_B_2FeatureDriver( ApplicationIdentityService s, FB_AFeatureDriver sb, F2_1FeatureDriver f2 ) : base( s )
        {
        }
    }

    [TestCase( true )]
    [TestCase( false )]
    public async Task feature_builders_initialization_follows_the_dependency_order_Async( bool revert )
    {
        CheckOrderFeatureDriver.Reset();
        var builderTypes = new List<Type>() { typeof( F1FeatureDriver ),
                                              typeof( F2_1FeatureDriver ),
                                              typeof( F3_2FeatureDriver ),
                                              typeof( FA_1FeatureDriver ),
                                              typeof( FB_AFeatureDriver ),
                                              typeof( FC_A_3FeatureDriver ),
                                              typeof( FD_B_2FeatureDriver ) };
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

        using var app = builder.AddApplicationIdentityServiceConfiguration()
                               .CKBuild();

        await app.StartAsync();
        var identityService = app.Services.GetRequiredService<ApplicationIdentityService>();

        var f1 = app.Services.GetRequiredService<F1FeatureDriver>();
        var f2_1 = app.Services.GetRequiredService<F2_1FeatureDriver>();
        var f3_2 = app.Services.GetRequiredService<F3_2FeatureDriver>();
        var fA_1 = app.Services.GetRequiredService<FA_1FeatureDriver>();
        var fB_A = app.Services.GetRequiredService<FB_AFeatureDriver>();
        var fC_A_3 = app.Services.GetRequiredService<FC_A_3FeatureDriver>();
        var fD_B_2 = app.Services.GetRequiredService<FD_B_2FeatureDriver>();
        CheckOrderFeatureDriver._count.ShouldBe( 7 );

        f1.FeatureName.ShouldBe( "F1" );
        f2_1.FeatureName.ShouldBe( "F2_1" );
        f3_2.FeatureName.ShouldBe( "F3_2" );
        fA_1.FeatureName.ShouldBe( "FA_1" );
        fB_A.FeatureName.ShouldBe( "FB_A" );
        fC_A_3.FeatureName.ShouldBe( "FC_A_3" );
        fD_B_2.FeatureName.ShouldBe( "FD_B_2" );

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
        protected NoopFeatureDriver( ApplicationIdentityService s, string? featureName, bool isAllowedByDefault )
            : base( s, featureName, isAllowedByDefault )
        {
        }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );
        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );
        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;
        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    // The "Asym" asymmetric feature: its common driver and its two sides.
    public sealed class AsymFeatureDriver : NoopFeatureDriver
    {
        public AsymFeatureDriver( ApplicationIdentityService s, bool isAllowedByDefault = false, string? featureName = "Asym" ) : base( s, featureName, isAllowedByDefault ) { }
    }

    public sealed class LocalAsymFeatureDriver : NoopFeatureDriver
    {
        public LocalAsymFeatureDriver( ApplicationIdentityService s, bool isAllowedByDefault = false, string featureName = "Asym" ) : base( s, featureName, isAllowedByDefault ) { }
    }

    public sealed class RemoteAsymFeatureDriver : NoopFeatureDriver
    {
        public RemoteAsymFeatureDriver( ApplicationIdentityService s, bool isAllowedByDefault = false ) : base( s, "Asym", isAllowedByDefault ) { }
    }

    public sealed class LocalStorageFeatureDriver : NoopFeatureDriver
    {
        public LocalStorageFeatureDriver( ApplicationIdentityService s ) : base( s, null, true ) { }
    }

    static ApplicationIdentityService CreateService( Action<MutableConfigurationSection>? configure = null )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "FakeDomain/$FakeApp";
            configure?.Invoke( c );
        } ).ShouldNotBeNull();
        return new ApplicationIdentityService( c, new ServiceCollection().BuildServiceProvider() );
    }

    [Test]
    public void an_asymmetric_feature_is_XXX_on_a_local_party_and_XXXRemote_on_a_remote()
    {
        // The local party is the "Asym": its remotes that allow "AsymRemote" use it.
        var s = CreateService( c =>
        {
            c["Local:AllowFeatures"] = "Asym";
            c["Parties:0:PartyName"] = "AllowsTheRemoteName";
            c["Parties:0:AllowFeatures"] = "AsymRemote";
            c["Parties:1:PartyName"] = "AllowsTheFeatureName";
            c["Parties:1:AllowFeatures"] = "Asym";
        } );
        var common = new AsymFeatureDriver( s );
        var local = new LocalAsymFeatureDriver( s );
        var remote = new RemoteAsymFeatureDriver( s );
        var byRemote = s.Remotes.ToDictionary( r => r.PartyName );
        foreach( var d in new ApplicationIdentityFeatureDriver[] { common, local, remote } )
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

        // The local party is not the "Asym": the remote that allows "AsymRemote" is the "Asym".
        s = CreateService( c =>
        {
            c["Parties:0:PartyName"] = "TheAsym";
            c["Parties:0:AllowFeatures"] = "AsymRemote";
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
            c["Defaults:AllowFeatures"] = "AsymRemote";
            c["Parties:0:PartyName"] = "A";
            c["Parties:1:PartyName"] = "B";
        } );
        var remote = new RemoteAsymFeatureDriver( s );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            remote.TryGetFeatureRemote( TestHelper.Monitor, s, out var r ).ShouldBeFalse();
            r.ShouldBeNull();
            texts.ShouldContain( t => t.Contains( "it can use only one remote that is the 'Asym' but the remotes 'FakeDomain/$A/#Dev', 'FakeDomain/$B/#Dev' allow 'AsymRemote'" ) );
        }
    }

    [TestCase( "Local" )]
    [TestCase( "Remote" )]
    [CancelAfter( 2000 )]
    public async Task a_local_party_that_allows_XXXRemote_or_a_remote_that_allows_XXX_is_an_error_Async( string side, CancellationToken cancellation )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "FakeDomain/$FakeApp";
            c["Local:AllowFeatures"] = "AsymRemote";
            c["Parties:0:PartyName"] = "Other";
            c["Parties:0:AllowFeatures"] = "Asym";
            // Allowing both names is fine (a "Defaults" applies to the local party and to the remotes).
            c["Parties:1:PartyName"] = "Both";
            c["Parties:1:AllowFeatures:0"] = "Asym";
            c["Parties:1:AllowFeatures:1"] = "AsymRemote";
        } ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddSingleton( c );
        services.AddSingleton<ApplicationIdentityService>();
        var t = side == "Local" ? typeof( LocalAsymFeatureDriver ) : typeof( RemoteAsymFeatureDriver );
        services.AddSingleton( t, sp => side == "Local"
                                            ? new LocalAsymFeatureDriver( sp.GetRequiredService<ApplicationIdentityService>() )
                                            : new RemoteAsymFeatureDriver( sp.GetRequiredService<ApplicationIdentityService>() ) );
        services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( t ) );

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
                "'Application: FakeDomain/$FakeApp/#Dev' allows the 'AsymRemote' feature in its \"Local\": 'AsymRemote' is allowed on a remote. The 'Asym' feature is asymmetric: a local party that is the 'Asym' allows 'Asym', a local party that uses a remote 'Asym' allows nothing.",
                "'FakeDomain/$Other/#Dev' allows the 'Asym' feature: 'Asym' is allowed in the \"Local\" of a local party. The 'Asym' feature is asymmetric: a remote that is the 'Asym' (or that uses the local 'Asym') allows 'AsymRemote'." ) ).ShouldBeTrue();
        }
    }

    [Test]
    public void the_drivers_of_an_asymmetric_feature_share_its_name()
    {
        var s = CreateService();
        new AsymFeatureDriver( s ).FeatureName.ShouldBe( "Asym" );
        new LocalAsymFeatureDriver( s ).FeatureName.ShouldBe( "Asym" );
        new RemoteAsymFeatureDriver( s ).FeatureName.ShouldBe( "Asym" );
        // Without the explicit feature name, the prefix is kept.
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
        Should.Throw<InvalidOperationException>( () => new RemoteAsymFeatureDriver( s ) )
              .Message.ShouldContain( "both manage the 'Asym' feature" );
    }

    [Test]
    public void the_drivers_of_a_feature_must_agree_on_isAllowedByDefault()
    {
        var s = CreateService();
        new AsymFeatureDriver( s, isAllowedByDefault: false );
        Should.Throw<InvalidOperationException>( () => new LocalAsymFeatureDriver( s, isAllowedByDefault: true ) )
              .Message.ShouldContain( "disagree on whether the 'Asym' feature is allowed by default" );
    }

    [Test]
    public void the_drivers_of_an_asymmetric_feature_must_give_its_name()
    {
        var s = CreateService();
        new AsymFeatureDriver( s, featureName: null );
        Should.Throw<InvalidOperationException>( () => new LocalAsymFeatureDriver( s ) )
              .Message.ShouldContain( "'CK.AppIdentity.Tests.FeatureBuilderInitializationTests.AsymFeatureDriver' doesn't give the feature name to the base constructor" );
    }

    [Test]
    public void the_explicit_feature_name_must_match_the_type_name()
    {
        Should.Throw<InvalidOperationException>( () => new LocalAsymFeatureDriver( CreateService(), featureName: "Other" ) )
              .Message.ShouldContain( "it must be 'OtherFeatureDriver', 'LocalOtherFeatureDriver' or 'RemoteOtherFeatureDriver'" );
    }

    [Test]
    public void two_drivers_of_the_same_feature_name_are_an_error()
    {
        var s = CreateService();
        new AsymFeatureDriver( s );
        Should.Throw<InvalidOperationException>( () => new AsymFeatureDriver( s ) )
              .Message.ShouldContain( "both manage the 'Asym' feature" );
        s = CreateService();
        new LocalStorageFeatureDriver( s );
        Should.Throw<InvalidOperationException>( () => new LocalStorageFeatureDriver( s ) )
              .Message.ShouldContain( "both manage the 'LocalStorage' feature" );
    }

}
