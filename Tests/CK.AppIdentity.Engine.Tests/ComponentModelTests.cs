using CK.AppIdentity.Engine.Tests.Modeled;
using CK.Core;
using CK.Monitoring;
using CK.Setup;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests.Modeled
{
    [ConfigurationModel]
    public sealed class ModeledFeatureConfiguration
    {
        public required string Name { get; init; }

        public int Count { get; init; } = 1;
    }

    /// <summary>
    /// A feature driver with a configuration model: it captures the model of the root party at setup.
    /// </summary>
    [PartyFeature( "ModeledFeature", TypeActivation.Optional )]
    [Configuration<ModeledFeatureConfiguration>]
    public sealed class ModeledFeatureDriver : ApplicationIdentityFeatureDriver
    {
        public ModeledFeatureDriver( ApplicationIdentityService s ) : base( s ) { }

        public ModeledFeatureConfiguration? SetupModel { get; private set; }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context )
        {
            if( IsRootAllowed ) SetupModel = GetModel<ModeledFeatureConfiguration>( ApplicationIdentityService );
            return Task.FromResult( true );
        }

        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    /// <summary>
    /// An option of the modeled feature: its name is a reserved key of the feature's section.
    /// </summary>
    [PartyFeatureOption( "Extra", TypeActivation.Optional )]
    public sealed class ExtraOption : IPartyComponentOption<ModeledFeatureDriver>
    {
    }

    /// <summary>
    /// The two sides of an asymmetric feature: only the Local side has a model (the "AsymRemote" key of a remote is the
    /// Remote side's).
    /// </summary>
    [PartyFeature( "Asym", TypeActivation.Optional, Side = FeatureSide.Local )]
    [Configuration<ModeledFeatureConfiguration>]
    public sealed class AsymLocalDriver : ApplicationIdentityFeatureDriver
    {
        public AsymLocalDriver( ApplicationIdentityService s ) : base( s ) { }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );

        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    [PartyFeature( "Asym", TypeActivation.Optional, Side = FeatureSide.Remote )]
    public sealed class AsymRemoteDriver : ApplicationIdentityFeatureDriver
    {
        public AsymRemoteDriver( ApplicationIdentityService s ) : base( s ) { }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );

        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    [ConfigurationModel]
    public sealed class ModeledClientConfiguration
    {
        public string? Title { get; init; }

        public int Width { get; init; } = 800;
    }

    [PartyClientFeature( "ModeledClient", TypeActivation.Optional )]
    [Configuration<ModeledClientConfiguration>]
    public sealed class ModeledClientFeature : ClientFeature
    {
        public ModeledClientFeature( PartyClient client, ImmutableConfigurationSection configuration, ModeledClientConfiguration model )
            : base( client, configuration )
        {
            Model = model;
        }

        public ModeledClientConfiguration Model { get; }
    }

    public sealed class ModeledClientFeatureDriver : ClientFeatureDriver<ModeledClientFeature, ModeledClientConfiguration>
    {
        protected override ModeledClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration, ModeledClientConfiguration model )
        {
            return new ModeledClientFeature( client, configuration, model );
        }
    }
}

namespace CK.AppIdentity.Engine.Tests
{
    /// <summary>
    /// The configuration models of the party components: a feature driver reads the model of each party (key by key
    /// along its levels, its options' names reserved), a client feature receives its model from its driver.
    /// </summary>
    [TestFixture]
    public class ComponentModelTests
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
                                                      typeof( ModeledFeatureDriver ),
                                                      typeof( ExtraOption ),
                                                      typeof( ModeledClientFeature ),
                                                      typeof( ModeledClientFeatureDriver ),
                                                      typeof( AsymLocalDriver ),
                                                      typeof( AsymRemoteDriver ) );
                _map = (await configuration.RunSuccessfullyAsync()).LoadMap();
            }
            return _map;
        }

        static Task<(AutomaticServices Services, ApplicationIdentityService Party)> StartAsync( Action<MutableConfigurationSection> configure )
        {
            return StartAsync( ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["StoreRootPath"] = TestHelper.TestProjectFolder.AppendPart( "ComponentModelStore" );
                c["FullName"] = "Test/$ModelHost";
                configure( c );
            } ).ShouldNotBeNull() );
        }

        // The configuration of a host (Microsoft's providers): a key can have a value and children.
        static Task<(AutomaticServices Services, ApplicationIdentityService Party)> StartAsync( Dictionary<string, string?> keys )
        {
            keys["CK-AppIdentity:StoreRootPath"] = TestHelper.TestProjectFolder.AppendPart( "ComponentModelStore" );
            keys["CK-AppIdentity:FullName"] = "Test/$ModelHost";
            var section = new ConfigurationBuilder().AddInMemoryCollection( keys ).Build().GetSection( "CK-AppIdentity" );
            return StartAsync( ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, section ).ShouldNotBeNull() );
        }

        static async Task<(AutomaticServices Services, ApplicationIdentityService Party)> StartAsync( ApplicationIdentityServiceConfiguration configuration )
        {
            var map = await GetMapAsync();
            var services = map.CreateAutomaticServices( services => services.AddSingleton( configuration ) );
            var party = services.Services.GetRequiredService<ApplicationIdentityService>();
            await ((IHostedService)party).StartAsync( default );
            await party.InitializationTask;
            return (services, party);
        }

        static async Task<string[]> FailAsync( Action<MutableConfigurationSection> configure )
        {
            using var logs = GrandOutput.Default!.CreateMemoryCollector( 5000 );
            await Should.ThrowAsync<Exception>( () => StartAsync( configure ) );
            return logs.ExtractCurrentTexts().ToArray();
        }

        [Test]
        public async Task the_engine_describes_the_model_of_a_component_Async()
        {
            var (services, party) = await StartAsync( c => { } );
            await using( services )
            {
                var d = services.Services.GetRequiredService<PartyComponentDirectory>();
                d.Find( typeof( ModeledFeatureDriver ) ).ShouldNotBeNull().Model.ShouldNotBeNull().ModelType.ShouldBe( typeof( ModeledFeatureConfiguration ) );
                d.Find( typeof( ModeledClientFeature ) ).ShouldNotBeNull().Model.ShouldNotBeNull().ModelType.ShouldBe( typeof( ModeledClientConfiguration ) );
                // Not allowed on the party: no model.
                Should.Throw<InvalidOperationException>( () => services.Services.GetRequiredService<ModeledFeatureDriver>().GetModel<ModeledFeatureConfiguration>( party ) );
                await ((IHostedService)party).StopAsync( default );
            }
        }

        [Test]
        public void the_analysis_of_the_components_describes_their_model()
        {
            // The analysis (used by the engine and by the test helpers of CK.Testing.AppIdentity) has the model.
            var cache = new CK.TypeCache.GlobalTypeCache();
            var result = PartyComponentAnalysis.Analyze( TestHelper.Monitor, [cache.Get( typeof( ModeledFeatureDriver ) ), cache.Get( typeof( ModeledClientFeature ) )] )
                                               .ShouldNotBeNull();
            result.Components.Single( c => c.ComponentType == typeof( ModeledFeatureDriver ) ).Model.ShouldNotBeNull().ModelType.ShouldBe( typeof( ModeledFeatureConfiguration ) );
            result.Components.Single( c => c.ComponentType == typeof( ModeledClientFeature ) ).Model.ShouldNotBeNull().ModelType.ShouldBe( typeof( ModeledClientConfiguration ) );
        }

        [Test]
        public async Task the_model_of_a_feature_is_read_key_by_key_along_the_levels_Async()
        {
            var (services, party) = await StartAsync( c =>
            {
                c["Defaults:ModeledFeature:Name"] = "FromDefaults";
                c["Defaults:ModeledFeature:Count"] = "2";
                c["Local:ModeledFeature:Count"] = "4";
                // An option of the feature: a reserved key of its section.
                c["Local:ModeledFeature:Extra"] = "true";
            } );
            await using( services )
            {
                var driver = services.Services.GetRequiredService<ModeledFeatureDriver>();
                var model = driver.SetupModel.ShouldNotBeNull();
                model.Name.ShouldBe( "FromDefaults" );
                model.Count.ShouldBe( 4 );
                driver.GetModel<ModeledFeatureConfiguration>( party ).ShouldBeSameAs( model );
                driver.GetOption<ExtraOption>( party ).ShouldNotBeNull();
                await ((IHostedService)party).StopAsync( default );
            }
        }

        [Test]
        public async Task a_section_with_a_value_and_keys_is_read_Async()
        {
            // An IConfiguration key can have a value and children: "true" with keys is a section.
            var (services, party) = await StartAsync( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:Local:ModeledFeature"] = "true",
                ["CK-AppIdentity:Local:ModeledFeature:Name"] = "WithKeys",
                ["CK-AppIdentity:Local:PartyClient:A:ModeledClient"] = "true",
                ["CK-AppIdentity:Local:PartyClient:A:ModeledClient:Width"] = "640"
            } );
            await using( services )
            {
                services.Services.GetRequiredService<ModeledFeatureDriver>().SetupModel.ShouldNotBeNull().Name.ShouldBe( "WithKeys" );
                party.GetRequiredFeature<PartyClientFeature>().Find( "A" ).ShouldNotBeNull()
                     .GetFeature<ModeledClientFeature>().ShouldNotBeNull().Model.Width.ShouldBe( 640 );
                await ((IHostedService)party).StopAsync( default );
            }
        }

        [Test]
        public async Task the_Local_side_of_an_asymmetric_feature_reads_the_model_of_the_local_parties_Async()
        {
            // The "AsymRemote" key of a remote is the Remote side's: the Local side's model is not read from it.
            var (services, party) = await StartAsync( c =>
            {
                c["Local:Asym:Name"] = "Local";
                c["Parties:0:PartyName"] = "Other";
                c["Parties:0:AsymRemote:Hint"] = "for the Remote side";
            } );
            await using( services )
            {
                var driver = services.Services.GetRequiredService<AsymLocalDriver>();
                driver.GetModel<ModeledFeatureConfiguration>( party ).Name.ShouldBe( "Local" );
                Should.Throw<InvalidOperationException>( () => driver.GetModel<ModeledFeatureConfiguration>( party.Remotes.Single() ) );
                await ((IHostedService)party).StopAsync( default );
            }
        }

        [Test]
        public async Task an_invalid_section_of_a_feature_fails_its_setup_Async()
        {
            var logs = await FailAsync( c =>
            {
                c["Local:ModeledFeature:Name"] = "A";
                c["Local:ModeledFeature:Nope"] = "1";
            } );
            logs.ShouldContain( t => t.Contains( "Nope" ), string.Join( Environment.NewLine, logs ) );
            logs = await FailAsync( c => c["Local:ModeledFeature"] = "true" );
            logs.ShouldContain( t => t.Contains( "Name" ), string.Join( Environment.NewLine, logs ) );
        }

        [Test]
        public async Task a_client_feature_receives_its_model_Async()
        {
            var (services, party) = await StartAsync( c =>
            {
                c["Local:PartyClient:A:ModeledClient:Title"] = "The title";
                c["Local:PartyClient:B:ModeledClient"] = "true";
            } );
            await using( services )
            {
                var clients = party.GetRequiredFeature<PartyClientFeature>();
                var a = clients.Find( "A" ).ShouldNotBeNull().GetFeature<ModeledClientFeature>().ShouldNotBeNull();
                a.Model.Title.ShouldBe( "The title" );
                a.Model.Width.ShouldBe( 800 );
                clients.Find( "B" ).ShouldNotBeNull().GetFeature<ModeledClientFeature>().ShouldNotBeNull().Model.Title.ShouldBeNull();
                await ((IHostedService)party).StopAsync( default );
            }
            var logs = await FailAsync( c => c["Local:PartyClient:A:ModeledClient:Width"] = "wide" );
            logs.ShouldContain( t => t.Contains( "Width" ), string.Join( Environment.NewLine, logs ) );
        }
    }
}
