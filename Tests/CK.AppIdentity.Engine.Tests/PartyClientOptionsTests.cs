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
/// The options of the party clients at runtime: the PartyClient feature selects them from the "Local:PartyClient:{Name}"
/// section of a client (the options of its type), creates them and gives them to the client.
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
                                                  typeof( ApplicationIdentityService ),
                                                  typeof( PartyClientFeatureDriver ),
                                                  typeof( FakeClientFactory ),
                                                  typeof( FakeClient ),
                                                  typeof( FakeDerivedClient ),
                                                  typeof( CachingOption ),
                                                  typeof( DerivedOnlyOption ) );
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

    [Test]
    public async Task the_options_of_a_client_are_selected_from_its_section_Async()
    {
        var (services, party) = await StartAsync( c =>
        {
            c["Local:PartyClient:Fake:Caching"] = "true";
            // A section enables the option and is its configuration; DerivedOnly comes after Caching.
            c["Local:PartyClient:Derived:Caching:Size"] = "42";
            c["Local:PartyClient:Derived:DerivedOnly"] = "true";
        } );
        await using( services )
        {
            Client( party, "Fake" ).Options.ShouldHaveSingleItem().ShouldBeOfType<CachingOption>();
            Client( party, "Derived" ).Options.Select( o => o.GetType() ).ShouldBe( [typeof( CachingOption ), typeof( DerivedOnlyOption )] );
            await ((IHostedService)party).StopAsync( default );
        }
    }

    [Test]
    public async Task a_client_without_options_has_none_Async()
    {
        var (services, party) = await StartAsync( c => c["Local:PartyClient:Fake"] = "" );
        await using( services )
        {
            Client( party, "Fake" ).Options.ShouldBeEmpty( "No option is enabled by default." );
            await ((IHostedService)party).StopAsync( default );
        }
    }

    [Test]
    public async Task an_option_of_another_kind_of_client_or_an_unknown_key_is_refused_Async()
    {
        // DerivedOnly is an option of FakeDerivedClient, not of FakeClient: its factory refuses the key (logged by the
        // agent of the party: the texts are collected from the GrandOutput).
        using var logs = GrandOutput.Default!.CreateMemoryCollector( 5000 );
        await Should.ThrowAsync<Exception>( () => StartAsync( c => c["Local:PartyClient:Fake:DerivedOnly"] = "true" ) );
        await Should.ThrowAsync<Exception>( () => StartAsync( c => c["Local:PartyClient:Fake:Unknown"] = "true" ) );
        var texts = logs.ExtractCurrentTexts();
        texts.ShouldContain( t => t.Contains( "Unexpected 'DerivedOnly' in \"Local:PartyClient:Fake\"" ) );
        texts.ShouldContain( t => t.Contains( "Unexpected 'Unknown' in \"Local:PartyClient:Fake\"" ) );
    }
}
