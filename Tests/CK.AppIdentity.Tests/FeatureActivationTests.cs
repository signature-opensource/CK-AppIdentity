using CK.Core;
using CK.Monitoring;
using CK.Testing;
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

/// <summary>
/// The declarations of the features follow their activation (<see cref="TypeActivation"/>): <c>false</c> on a required
/// feature is a start error, a redundant <c>true</c> or <c>false</c> is a warning, a required feature is always allowed.
/// </summary>
[TestFixture]
public class FeatureActivationTests
{
    [PartyFeature( "OptIn", TypeActivation.Optional )]
    public sealed class OptInFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        public OptInFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    [PartyFeature( "OptOut", TypeActivation.ByDefault )]
    public sealed class OptOutFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        public OptOutFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    [PartyFeature( "Must", TypeActivation.Required )]
    public sealed class MustFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        public MustFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    // Needy requires OptIn: a party that has Needy must have OptIn.
    [PartyFeature( "Needy", TypeActivation.Optional )]
    [Requires<OptInFeatureDriver>]
    public sealed class NeedyFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        public NeedyFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
    }

    static async Task<(ServiceProvider Services, ApplicationIdentityService Service)> StartAsync( Action<MutableConfigurationSection> configure,
                                                                                                 CancellationToken cancel,
                                                                                                 Dictionary<string, IEnumerable<Type>>? typeSets = null )
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Activation/$App";
            c["Parties:0:PartyName"] = "Remote";
            configure( c );
        } ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddSingleton( c );
        services.AddSingleton<ApplicationIdentityService>();
        services.AddSingleton<OptInFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<OptInFeatureDriver>() );
        services.AddSingleton<OptOutFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<OptOutFeatureDriver>() );
        services.AddSingleton<MustFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MustFeatureDriver>() );
        services.AddSingleton<NeedyFeatureDriver>();
        services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<NeedyFeatureDriver>() );
        services.AddSingleton( AppIdentityTestHelper.CreateComponentDirectory( [typeof( OptInFeatureDriver ), typeof( OptOutFeatureDriver ), typeof( MustFeatureDriver ), typeof( NeedyFeatureDriver )], typeSets ) );
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
    public async Task the_activation_gives_the_default_and_a_required_feature_is_always_allowed_Async( CancellationToken cancel )
    {
        var (sp, s) = await StartAsync( c => { }, cancel );
        await using( sp )
        {
            await s.InitializationTask.WaitAsync( cancel );
            sp.GetRequiredService<OptInFeatureDriver>().IsAllowedFeature( s ).ShouldBeFalse();
            sp.GetRequiredService<OptOutFeatureDriver>().IsAllowedFeature( s ).ShouldBeTrue();
            var must = sp.GetRequiredService<MustFeatureDriver>();
            must.Activation.ShouldBe( TypeActivation.Required );
            must.IsAllowedFeature( s ).ShouldBeTrue();
            must.IsAllowedFeature( s.Remotes.Single() ).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task a_required_feature_cannot_be_turned_off_Async( CancellationToken cancel )
    {
        using var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 200 );
        var (sp, s) = await StartAsync( c => c["Parties:0:Must"] = "false", cancel );
        await using( sp )
        {
            await Should.ThrowAsync<CKException>( () => s.InitializationTask.WaitAsync( cancel ) );
            var texts = await CollectAsync( logs, "The feature 'Must' is required" );
            texts.ShouldContain( t => t.StartsWith( "The feature 'Must' is required: it can't be turned off by '" ) && t.Contains( "Activation/$Remote" ) );
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task a_redundant_declaration_is_a_warning_Async( CancellationToken cancel )
    {
        using var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 200 );
        var (sp, s) = await StartAsync( c =>
        {
            c["Local:OptIn"] = "false";
            c["Local:OptOut"] = "true";
            c["Local:Must"] = "true";
            // A section is never redundant: it is the configuration of the feature.
            c["Parties:0:OptOut:Some"] = "Setting";
        }, cancel );
        await using( sp )
        {
            await s.InitializationTask.WaitAsync( cancel );
            var texts = await CollectAsync( logs, "Redundant \"Must\"" );
            texts.ShouldContain( t => t.StartsWith( "Redundant \"OptIn\": false: the feature is optional" ) );
            texts.ShouldContain( t => t.StartsWith( "Redundant \"OptOut\": true: the feature is allowed by default" ) && !t.Contains( "Activation/$Remote" ) );
            texts.ShouldContain( t => t.StartsWith( "Redundant \"Must\": true: the feature is required" ) );
        }
    }

    // The set of "$App" doesn't contain OptOut: the types drive.
    static readonly Dictionary<string, IEnumerable<Type>> _restricted = new() { { "$App", [typeof( OptInFeatureDriver ), typeof( MustFeatureDriver )] } };

    [Test]
    [CancelAfter( 5000 )]
    public async Task a_feature_that_is_not_a_member_of_the_set_of_a_party_does_not_apply_to_it_Async( CancellationToken cancel )
    {
        var (sp, s) = await StartAsync( c => c["Local:OptOut"] = "false", cancel, _restricted );
        await using( sp )
        {
            await s.InitializationTask.WaitAsync( cancel );
            var optOut = sp.GetRequiredService<OptOutFeatureDriver>();
            optOut.IsMember( s ).ShouldBeFalse();
            optOut.IsAllowedFeature( s ).ShouldBeFalse( "Allowed by default but not a member." );
            optOut.IsAllowedFeature( s.Remotes.Single() ).ShouldBeFalse( "A remote uses the set of its local party." );
            sp.GetRequiredService<MustFeatureDriver>().IsAllowedFeature( s ).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task declaring_a_feature_that_is_not_a_member_is_an_error_Async( CancellationToken cancel )
    {
        using var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 200 );
        var (sp, s) = await StartAsync( c => c["Parties:0:OptOut:Some"] = "Setting", cancel, _restricted );
        await using( sp )
        {
            await Should.ThrowAsync<CKException>( () => s.InitializationTask.WaitAsync( cancel ) );
            var texts = await CollectAsync( logs, "The feature 'OptOut' is declared by" );
            texts.ShouldContain( t => t.StartsWith( "The feature 'OptOut' is declared by '" ) && t.Contains( "Activation/$Remote" ) && t.Contains( "is not a member of the type set of their party" ) );
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task the_scope_of_a_party_is_checked_at_start_Async( CancellationToken cancel )
    {
        using var logs = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 200 );
        var (sp, s) = await StartAsync( c => c["Local:Needy"] = "true", cancel );
        await using( sp )
        {
            await Should.ThrowAsync<CKException>( () => s.InitializationTask.WaitAsync( cancel ) );
            var texts = await CollectAsync( logs, "requires 'OptInFeatureDriver'" );
            texts.ShouldContain( t => t.StartsWith( "'NeedyFeatureDriver' of 'Application: Activation/$App" ) && t.Contains( "requires 'OptInFeatureDriver' that" ) );
        }
        (sp, s) = await StartAsync( c => { c["Local:Needy"] = "true"; c["Local:OptIn"] = "true"; }, cancel );
        await using( sp )
        {
            await s.InitializationTask.WaitAsync( cancel );
        }
    }
}
