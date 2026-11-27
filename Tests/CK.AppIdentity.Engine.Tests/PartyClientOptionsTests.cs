using CK.AppIdentity.Engine.Tests.Fake;
using CK.Core;
using CK.Monitoring;
using CK.Setup;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests;

/// <summary>
/// The client features at runtime: the PartyClient feature creates the features of a client from its
/// "Local:PartyClient:{Name}" section (a key is a feature), checks their requirements (the generated PartyComponentDirectory),
/// orders them, then selects their options from their section, creates them and gives them to the feature.
/// </summary>
[TestFixture]
public class PartyClientOptionsTests
{
    static IStObjMap? _map;

    static async Task<IStObjMap> GetMapAsync()
    {
        if( _map == null )
        {
            var configuration = TestHelper.CreateDefaultEngineConfiguration();
            configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ),
                                                  typeof( PartyComponentDirectory ),
                                                  typeof( ApplicationIdentityService ),
                                                  typeof( PartyClientFeatureDriver ),
                                                  typeof( FakePartyFeatureDriver ),
                                                  typeof( FakeClientFeatureDriver ),
                                                  typeof( FakeDerivedClientFeatureDriver ),
                                                  typeof( FakeClientFeature ),
                                                  typeof( FakeDerivedClientFeature ),
                                                  typeof( CachingOption ),
                                                  typeof( DerivedOnlyOption ),
                                                  typeof( BuildClientFeature ),
                                                  typeof( OtherBuildClientFeature ),
                                                  typeof( ServeClientFeature ),
                                                  typeof( WindowOption ),
                                                  typeof( NeedsFakeClientFeature ),
                                                  typeof( BuildClientFeatureDriver ),
                                                  typeof( OtherBuildClientFeatureDriver ),
                                                  typeof( ServeClientFeatureDriver ),
                                                  typeof( NeedsFakeClientFeatureDriver ) );
            _map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        }
        return _map;
    }

    // Starts a party whose "Local:PartyClient" section is the given one (a JSON object).
    static async Task<(AutomaticServices Services, ApplicationIdentityService Party)> StartAsync( Action<MutableConfigurationSection> clients )
    {
        var map = await GetMapAsync();
        var store = TestHelper.TestProjectFolder.AppendPart( "PartyClientStore" );
        var services = map.CreateAutomaticServices( services =>
        {
            services.AddSingleton( ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["StoreRootPath"] = store;
                c["FullName"] = "Test/$OptionsHost";
                clients( c );
            } ).ShouldNotBeNull() );
        } );
        var party = services.Services.GetRequiredService<ApplicationIdentityService>();
        await ((IHostedService)party).StartAsync( default );
        await party.InitializationTask;
        return (services, party);
    }

    static PartyClient Client( ApplicationIdentityService party, string name ) => party.GetRequiredFeature<PartyClientFeature>().Find( name ).ShouldNotBeNull();

    // Starts a party that must fail and returns the logs (logged by the agent of the party: the texts are collected from
    // the GrandOutput).
    static async Task<string[]> FailAsync( Action<MutableConfigurationSection> clients )
    {
        using var logs = GrandOutput.Default!.CreateMemoryCollector( 5000 );
        // A configuration error fails the start in strict mode (the "#Dev" party of these tests is lenient by default).
        await Should.ThrowAsync<Exception>( () => StartAsync( c => { ComponentModelTests.Strict( c ); clients( c ); } ) );
        return logs.ExtractCurrentTexts().ToArray();
    }

    [Test]
    public async Task the_engine_computes_the_requirements_the_abstractions_and_the_order_Async()
    {
        var (services, party) = await StartAsync( c => { } );
        await using( services )
        {
            var d = services.Services.GetRequiredService<PartyComponentDirectory>();
            var serve = d.Find( typeof( ServeClientFeature ) ).ShouldNotBeNull();
            serve.Requires.ShouldBe( [typeof( IFakeBuild )] );
            d.Find( typeof( BuildClientFeature ) ).ShouldNotBeNull().Abstractions.ShouldBe( [typeof( IFakeBuild )] );
            d.Find( typeof( NeedsFakeClientFeature ) ).ShouldNotBeNull().Requires.ShouldBe( [typeof( FakePartyFeatureDriver )] );
            d.IndexOf( typeof( ServeClientFeature ) ).ShouldBeGreaterThan( d.IndexOf( typeof( BuildClientFeature ) ) );
            d.IndexOf( typeof( ServeClientFeature ) ).ShouldBeGreaterThan( d.IndexOf( typeof( OtherBuildClientFeature ) ) );
            d.IndexOf( typeof( NeedsFakeClientFeature ) ).ShouldBeGreaterThan( d.IndexOf( typeof( FakePartyFeatureDriver ) ) );
            d.IsMultipleAbstraction( typeof( IFakeBuild ) ).ShouldBeFalse();
            await ((IHostedService)party).StopAsync( default );
        }
    }

    [Test]
    public async Task the_options_of_a_feature_are_selected_from_its_section_Async()
    {
        var (services, party) = await StartAsync( c =>
        {
            c["Local:PartyClient:A:Fake:Caching"] = "true";
            // A section enables the option and is its configuration; DerivedOnly comes after Caching.
            c["Local:PartyClient:B:FakeDerived:Caching:Size"] = "42";
            c["Local:PartyClient:B:FakeDerived:DerivedOnly"] = "true";
        } );
        await using( services )
        {
            Client( party, "A" ).Features.ShouldHaveSingleItem().ShouldBeOfType<FakeClientFeature>()
                                .Options.ShouldHaveSingleItem().ShouldBeOfType<CachingOption>();
            Client( party, "B" ).GetFeature<FakeDerivedClientFeature>().ShouldNotBeNull()
                                .Options.Select( o => o.GetType() ).ShouldBe( [typeof( CachingOption ), typeof( DerivedOnlyOption )] );
            await ((IHostedService)party).StopAsync( default );
        }
    }

    [Test]
    public async Task a_feature_without_options_has_none_Async()
    {
        var (services, party) = await StartAsync( c => c["Local:PartyClient:A:Fake"] = "true" );
        await using( services )
        {
            Client( party, "A" ).Features.ShouldHaveSingleItem().Options.ShouldBeEmpty( "No option is enabled by default." );
            Client( party, "A" ).Description.ShouldBeNull( "No feature gives a request path." );
            await ((IHostedService)party).StopAsync( default );
        }
    }

    [Test]
    public async Task an_option_of_another_kind_of_feature_or_an_unknown_key_is_refused_Async()
    {
        // DerivedOnly is an option of FakeDerivedClientFeature, not of FakeClientFeature (that has no model): the key is refused.
        var texts = await FailAsync( c => c["Local:PartyClient:A:Fake:DerivedOnly"] = "true" );
        texts.ShouldContain( t => t.Contains( "Unknown key \"CK-AppIdentity:Local:PartyClient:A:Fake:DerivedOnly\": the client feature 'Fake' has no configuration of its own" ) );
        texts = await FailAsync( c => c["Local:PartyClient:A:Unknown"] = "true" );
        texts.ShouldContain( t => t.Contains( "Unknown \"CK-AppIdentity:Local:PartyClient:A:Unknown\": the keys of a client are its features" ) );
        texts = await FailAsync( c => c["Local:PartyClient:A:Types:0"] = "Some.Type, Some.Assembly" );
        texts.ShouldContain( t => t.Contains( "The client 'A' of 'Test/$OptionsHost" ) && t.Contains( "has no feature." ) );
    }

    [Test]
    public async Task an_option_may_require_another_feature_of_its_client_Async()
    {
        // Window (an option of Build) requires Serve, that is created after Build: the options are selected once every
        // feature of the client is created.
        var (services, party) = await StartAsync( c =>
        {
            c["Local:PartyClient:App:Build:Window"] = "true";
            c["Local:PartyClient:App:Serve"] = "true";
        } );
        await using( services )
        {
            Client( party, "App" ).GetFeature<BuildClientFeature>().ShouldNotBeNull().Options.ShouldHaveSingleItem().ShouldBeOfType<WindowOption>();
            await ((IHostedService)party).StopAsync( default );
        }
        var texts = await FailAsync( c => c["Local:PartyClient:App:Build:Window"] = "true" );
        texts.ShouldContain( t => t.Contains( "Window" ) && t.Contains( "the client feature 'ServeClientFeature'" ), string.Join( Environment.NewLine, texts ) );
    }

    [Test]
    public async Task the_models_of_an_option_can_be_read_before_the_setup_Async()
    {
        // What a desktop application needs before its host starts: the clients that enable an option and its model.
        var map = await GetMapAsync();
        await using var services = map.CreateAutomaticServices( s =>
        {
            s.AddSingleton( ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["StoreRootPath"] = TestHelper.TestProjectFolder.AppendPart( "PartyClientStore" );
                c["FullName"] = "Test/$OptionsHost";
                c["Local:PartyClient:App:Build:Window:Title"] = "The title";
                c["Local:PartyClient:App:Serve"] = "true";
                c["Local:PartyClient:Other:Build:Window"] = "false";
                c["Local:PartyClient:Third:Build"] = "true";
            } ).ShouldNotBeNull() );
        } );
        var driver = services.Services.GetRequiredService<PartyClientFeatureDriver>();
        driver.TryFindOptionModels( TestHelper.Monitor, typeof( WindowOption ), out var found ).ShouldBeTrue();
        var (name, model) = found.ShouldHaveSingleItem();
        name.ShouldBe( "App" );
        model.ShouldBeOfType<WindowOptionConfiguration>().Title.ShouldBe( "The title" );
    }

    [Test]
    public async Task a_compile_time_key_is_unknown_at_run_time_Async()
    {
        // Build has no configuration model: only its options are accepted. The keys of its compile time model are the
        // build's: the host's "appsettings.json" never has them.
        var texts = await FailAsync( c => c["Local:PartyClient:App:Build:Port"] = "3000" );
        texts.ShouldContain( t => t.Contains( "Unknown key \"CK-AppIdentity:Local:PartyClient:App:Build:Port\"" ) && t.Contains( "(Window)" ) );
    }

    [Test]
    public async Task the_features_are_ordered_by_their_requirements_and_describe_the_client_Async()
    {
        var (services, party) = await StartAsync( c =>
        {
            c["Local:PartyClient:App:Serve"] = "true";
            c["Local:PartyClient:App:Build"] = "true";
            c["Local:PartyClient:App:Types:0"] = "Some.Type, Some.Assembly";
        } );
        await using( services )
        {
            var app = Client( party, "App" );
            app.Features.Select( f => f.Name ).ShouldBe( ["Build", "Serve"], "Serve requires IFakeBuild: it comes after Build." );
            app.GetFeature<IFakeBuild>().ShouldBeOfType<BuildClientFeature>();
            app.Description.ShouldNotBeNull().RequestPath.ShouldBe( "/" );
            await ((IHostedService)party).StopAsync( default );
        }
    }

    [Test]
    public async Task the_requirements_of_the_features_are_checked_Async()
    {
        var texts = await FailAsync( c => c["Local:PartyClient:App:Serve"] = "true" );
        texts.ShouldContain( t => t.Contains( "'ServeClientFeature' of client 'App' of 'Test/$OptionsHost" ) && t.Contains( "requires 'IFakeBuild' that client 'App'" ) );

        texts = await FailAsync( c =>
        {
            c["Local:PartyClient:App:Build"] = "true";
            c["Local:PartyClient:App:OtherBuild"] = "true";
        } );
        texts.ShouldContain( t => t.Contains( "has more than one 'IFakeBuild' ('BuildClientFeature', 'OtherBuildClientFeature'): it can have at most one." ) );

        // The FakePartyFeatureDriver is not allowed by default.
        texts = await FailAsync( c => c["Local:PartyClient:App:NeedsFake"] = "true" );
        texts.ShouldContain( t => t.Contains( "'NeedsFakeClientFeature' of client 'App'" ) && t.Contains( "requires 'FakePartyFeatureDriver'" ) );
        // Allowed: fine.
        var (services, party) = await StartAsync( c =>
        {
            c["Local:FakeParty"] = "true";
            c["Local:PartyClient:App:NeedsFake"] = "true";
        } );
        await using( services )
        {
            Client( party, "App" ).GetFeature<NeedsFakeClientFeature>().ShouldNotBeNull();
            await ((IHostedService)party).StopAsync( default );
        }
    }
}