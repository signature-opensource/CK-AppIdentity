using CK.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        s.DomainName.Should().Be( "D" );
        s.EnvironmentName.Should().Be( "#Production" );
        s.PartyName.Should().Be( "$MyApp" );
        s.Features.Should().BeEmpty();

        s.Parties.Should().HaveCount( 2 );
        var r1 = s.Remotes.Single( r => r.PartyName == "$Remote1" );
        r1.IsDynamic.Should().BeFalse();
        r1.Address.Should().BeNull();
        r1.DomainName.Should().Be( "D" );
        r1.EnvironmentName.Should().Be( "#Production" );
        r1.Features.Should().BeEmpty();

        var r2 = s.Remotes.Single( r => r.PartyName == "$Remote2" );
        r2.IsDynamic.Should().BeFalse();
        r2.Address.Should().BeNull();
        r2.DomainName.Should().Be( "D" );
        r2.EnvironmentName.Should().Be( "#Production" );
        r2.Features.Should().BeEmpty();

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
            context.Memory.GetValueOrDefault( "DynamicSetupOrder", 0 ).Should().Be( SetupOrder );
            context.Memory["DynamicSetupOrder"] = SetupOrder + 1;
            return Task.FromResult( true );
        }

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty remote )
        {
            _dynamicTeardownCount++;
            context.Monitor.Trace( $"TeardownDynamic {GetType().Name} ({SetupOrder})." );
            int revertOrder = _currentSetupOrder - SetupOrder - 1;
            context.Memory.GetValueOrDefault( "DynamicTeardownOrder", 0 ).Should().Be( revertOrder );
            // Next expected value.
            context.Memory["DynamicTeardownOrder"] = revertOrder + 1;
            return Task.CompletedTask;
        }

        protected override Task TeardownAsync( FeatureLifetimeContext context )
        {
            _teardownCount++;
            context.Monitor.Trace( $"Teardown {GetType().Name} ({SetupOrder})." );
            int revertOrder = _currentSetupOrder - SetupOrder - 1;
            context.Memory.GetValueOrDefault( "TeardownOrder", 0 ).Should().Be( revertOrder );
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
        CheckOrderFeatureDriver._count.Should().Be( 7 );

        f1.FeatureName.Should().Be( "F1" );
        f2_1.FeatureName.Should().Be( "F2_1" );
        f3_2.FeatureName.Should().Be( "F3_2" );
        fA_1.FeatureName.Should().Be( "FA_1" );
        fB_A.FeatureName.Should().Be( "FB_A" );
        fC_A_3.FeatureName.Should().Be( "FC_A_3" );
        fD_B_2.FeatureName.Should().Be( "FD_B_2" );

        f1.SetupOrder.Should().Be( 0 );
        f2_1.SetupOrder.Should().BeGreaterThan( f1.SetupOrder );
        f3_2.SetupOrder.Should().BeGreaterThan( f2_1.SetupOrder );
        fA_1.SetupOrder.Should().BeGreaterThan( f1.SetupOrder );
        fB_A.SetupOrder.Should().BeGreaterThan( fA_1.SetupOrder );
        fC_A_3.SetupOrder.Should().BeGreaterThan( fA_1.SetupOrder ).And.BeGreaterThan( f3_2.SetupOrder );
        fD_B_2.SetupOrder.Should().BeGreaterThan( fB_A.SetupOrder ).And.BeGreaterThan( f2_1.SetupOrder );

        var r = await identityService.AddRemoteAsync( TestHelper.Monitor, c =>
        {
            c["PartyName"] = "SomeDynamicRemote";
        } );
        Throw.DebugAssert( r != null );
        CheckOrderFeatureDriver._dynamicSetupCount.Should().Be( 7 );
        CheckOrderFeatureDriver._dynamicTeardownCount.Should().Be( 0 );

        await r.DestroyAsync();
        CheckOrderFeatureDriver._dynamicTeardownCount.Should().Be( 7 );
        CheckOrderFeatureDriver._teardownCount.Should().Be( 0 );

        await identityService.DisposeAsync();
        CheckOrderFeatureDriver._teardownCount.Should().Be( 7 );
    }
}
