using CK.Core;
using CK.Testing;
using CK.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class FeatureNamesTests
{
    // Allowed by default: only "OptOut": false switches it off.
    [PartyFeature( "OptOut", TypeActivation.ByDefault )]
    public sealed class OptOutFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        public OptOutFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    static async Task<(ServiceProvider Services, ApplicationIdentityService Service)> StartAsync( Action<MutableConfigurationSection> configure, CancellationToken cancel )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Names/$App";
            c["Parties:0:PartyName"] = "Remote";
            configure( c );
        } ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddSingleton( c );
        services.AddSingleton<ApplicationIdentityService>();
        services.AddSingleton<OptOutFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<OptOutFeatureDriver>() );
        services.AddSingleton<FeatureBuilderInitializationTests.LocalAsymFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<FeatureBuilderInitializationTests.LocalAsymFeatureDriver>() );
        services.AddSingleton( AppIdentityTestHelper.CreateComponentDirectory( typeof( OptOutFeatureDriver ), typeof( FeatureBuilderInitializationTests.LocalAsymFeatureDriver ) ) );
        var sp = services.BuildServiceProvider();
        var s = sp.GetRequiredService<ApplicationIdentityService>();
        await ((IHostedService)s).StartAsync( cancel );
        return (sp, s);
    }

    // The GrandOutput dispatches the logs asynchronously.
    static async Task<List<string>> CollectAsync( GrandOutputMemoryCollector logs, string expected )
    {
        var all = new List<string>();
        for( int i = 0; i < 50; ++i )
        {
            all.AddRange( logs.ExtractCurrentTexts() );
            if( all.Any( t => t.Contains( expected ) ) ) break;
            await Task.Delay( 20 );
        }
        return all;
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task feature_names_are_case_insensitive_Async( CancellationToken cancel )
    {
        var (sp, s) = await StartAsync( c =>
        {
            c["Local:OPTOUT"] = "false";
            c["Parties:0:asymremote"] = "true";
        }, cancel );
        await using( sp )
        {
            await s.InitializationTask.WaitAsync( cancel );
            var optOut = sp.GetRequiredService<OptOutFeatureDriver>();
            optOut.IsAllowedFeature( s ).ShouldBeFalse( "'OPTOUT': false turns 'OptOut' off." );
            optOut.IsAllowedFeature( s.Remotes.Single() ).ShouldBeTrue();
            sp.GetRequiredService<FeatureBuilderInitializationTests.LocalAsymFeatureDriver>().IsAllowedFeature( s.Remotes.Single() ).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task an_unknown_feature_name_is_a_warning_Async( CancellationToken cancel )
    {
        using var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 200 );
        var (sp, s) = await StartAsync( c =>
        {
            c["Local:OptOutt"] = "false";
            // "Asym" on a local party and "AsymRemote" on a remote are known names.
            c["Parties:0:OptOutt"] = "false";
            c["Parties:0:AsymRemote"] = "false";
        }, cancel );
        await using( sp )
        {
            await s.InitializationTask.WaitAsync( cancel );
            sp.GetRequiredService<OptOutFeatureDriver>().IsAllowedFeature( s ).ShouldBeTrue( "The typo turns nothing off." );
            var texts = await CollectAsync( logs, "Unknown feature 'OptOutt'" );
            texts.ShouldContain( t => t.StartsWith( "Unknown feature 'OptOutt' declared by '" )
                                      && t.Contains( "Names/$App" ) && t.Contains( "Names/$Remote" ) );
            texts.ShouldNotContain( t => t.Contains( "Unknown feature 'AsymRemote'" ) );
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task an_unknown_feature_name_fails_the_start_in_StrictConfigurationMode_Async( CancellationToken cancel )
    {
        var (sp, s) = await StartAsync( c =>
        {
            c["StrictConfigurationMode"] = "true";
            c["Parties:0:Tranport"] = "false";
        }, cancel );
        await using( sp )
        {
            await Should.ThrowAsync<CKException>( () => s.InitializationTask.WaitAsync( cancel ) );
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task an_unknown_feature_name_of_a_dynamic_party_fails_its_addition_in_StrictConfigurationMode_Async( CancellationToken cancel )
    {
        var (sp, s) = await StartAsync( c => c["StrictConfigurationMode"] = "true", cancel );
        await using( sp )
        {
            await s.InitializationTask.WaitAsync( cancel );
            (await s.AddRemoteAsync( TestHelper.Monitor, c =>
            {
                c["PartyName"] = "Dynamic";
                c["OptOutt"] = "false";
            } )).ShouldBeNull();
            (await s.AddRemoteAsync( TestHelper.Monitor, c =>
            {
                c["PartyName"] = "Dynamic";
                c["optout"] = "false";
            } )).ShouldNotBeNull();
        }
    }
}
