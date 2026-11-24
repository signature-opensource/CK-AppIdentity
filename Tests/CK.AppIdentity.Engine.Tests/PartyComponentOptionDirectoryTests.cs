using CK.AppIdentity.Engine.Tests.Fake;
using CK.Core;
using CK.Setup;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests;

[TestFixture]
public class PartyComponentOptionDirectoryTests
{
    static readonly Type[] _model = [typeof( PartyComponentOptionDirectory ), typeof( PartyComponentDirectory ), typeof( ApplicationIdentityService ),
                                     typeof( FakePartyFeatureDriver ), typeof( MainOption ), typeof( HeaderOption ), typeof( CompressionOption ),
                                     typeof( FakeClientFeature ), typeof( FakeDerivedClientFeature ), typeof( CachingOption ), typeof( DerivedOnlyOption )];

    static async Task<AutomaticServices> CreateServicesAsync()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( _model );
        var r = await configuration.RunSuccessfullyAsync();
        return r.LoadMap().CreateAutomaticServices( services =>
        {
            services.AddSingleton<FakeService>();
            services.AddSingleton( ApplicationIdentityServiceConfiguration.CreateEmpty( "Test", "Options" ) );
        } );
    }

    [Test]
    public async Task options_are_ordered_with_their_requirements_Async()
    {
        await using var auto = await CreateServicesAsync();
        var directory = auto.Services.GetRequiredService<PartyComponentOptionDirectory>();

        directory.OwnerTypes.ShouldBe( [typeof( FakeDerivedClientFeature ), typeof( FakePartyFeatureDriver ), typeof( FakeClientFeature )], ignoreOrder: true );

        var driver = directory.GetOptions( typeof( FakePartyFeatureDriver ) );
        driver.Select( o => o.Name ).ShouldBe( ["Header", "Main", "Compression"] );
        driver.Select( o => o.IsEnabledByDefault ).ShouldBe( [true, false, true] );
        // The optional relation of Header is an order only. Compression requires Main.
        driver[0].Requires.ShouldBeEmpty();
        driver[1].Requires.ShouldBeEmpty();
        driver[2].Requires.ShouldBe( ["Main"] );

        // The options of a base owner type apply to the derived one.
        directory.GetOptions( typeof( FakeClientFeature ) ).Select( o => o.Name ).ShouldBe( ["Caching"] );
        var derived = directory.GetOptions( typeof( FakeDerivedClientFeature ) );
        derived.Select( o => o.Name ).ShouldBe( ["Caching", "DerivedOnly"] );
        derived[1].Requires.ShouldBe( ["Caching"] );

        directory.GetOptions( typeof( string ) ).ShouldBeEmpty();
        directory.IsOptionName( typeof( FakePartyFeatureDriver ), "compression" ).ShouldBeTrue();
        directory.IsOptionName( typeof( FakeClientFeature ), "DerivedOnly" ).ShouldBeFalse();
    }

    [Test]
    public async Task RequiredBy_makes_the_target_require_the_declaring_option_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ), typeof( ApplicationIdentityService ),
                                              typeof( FakePartyFeatureDriver ), typeof( MainOption ), typeof( TracingOption ) );
        var r = await configuration.RunSuccessfullyAsync();
        await using var auto = r.LoadMap().CreateAutomaticServices( services =>
        {
            services.AddSingleton( ApplicationIdentityServiceConfiguration.CreateEmpty( "Test", "Options" ) );
        } );
        var options = auto.Services.GetRequiredService<PartyComponentOptionDirectory>().GetOptions( typeof( FakePartyFeatureDriver ) );
        options.Select( o => o.Name ).ShouldBe( ["Tracing", "Main"] );
        // [RequiredBy<MainOption>] on Tracing: Main requires Tracing, Tracing requires nothing.
        options[0].Requires.ShouldBeEmpty();
        options[1].Requires.ShouldBe( ["Tracing"] );

        ConfigurationOptionSelector.TrySelect( TestHelper.Monitor, options, Section( """{ "Tracing": true }""" ), out var selected ).ShouldBeTrue();
        selected.Select( s => s.Option.Name ).ShouldBe( ["Tracing"] );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            ConfigurationOptionSelector.TrySelect( TestHelper.Monitor, options, Section( """{ "Main": true, "Tracing": false }""" ), out _ ).ShouldBeFalse();
            texts.ShouldContain( "The option 'Main' requires the option 'Tracing' that is disabled." );
        }
    }

    [Test]
    public async Task an_option_can_require_a_party_feature_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ), typeof( ApplicationIdentityService ),
                                              typeof( FakePartyFeatureDriver ), typeof( FakeMailFeatureDriver ), typeof( NotifyOption ) );
        var r = await configuration.RunSuccessfullyAsync();
        await using var auto = r.LoadMap().CreateAutomaticServices( services =>
        {
            services.AddSingleton( ApplicationIdentityServiceConfiguration.CreateEmpty( "Test", "Options" ) );
        } );
        var directory = auto.Services.GetRequiredService<PartyComponentOptionDirectory>();
        var notify = directory.GetOptions( typeof( FakePartyFeatureDriver ) ).ShouldHaveSingleItem();
        // Outside the options of its owner: not a requirement between options, a required party feature.
        notify.Requires.ShouldBeEmpty();
        notify.RequiredFeatures.ShouldBe( [typeof( FakeMailFeatureDriver )] );

        // "Only with it": the level gives the missing requirement (the feature not allowed on the party of the owner).
        static string? MailOff( PartyComponentOptionInfo o ) => o.RequiredFeatures.Count > 0 ? "the party feature 'FakeMail'" : null;
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            directory.TryCreate( TestHelper.Monitor, typeof( FakePartyFeatureDriver ), null, auto.Services, out var created, MailOff ).ShouldBeTrue();
            created.ShouldBeEmpty();
            texts.ShouldContain( "The option 'Notify' (enabled by default) is disabled: it requires the party feature 'FakeMail' that is disabled." );
        }
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            directory.TryCreate( TestHelper.Monitor, typeof( FakePartyFeatureDriver ), Section( """{ "Notify": true }""" ), auto.Services, out _, MailOff ).ShouldBeFalse();
            texts.ShouldContain( "The option 'Notify' requires the party feature 'FakeMail' that is disabled." );
        }
        directory.TryCreate( TestHelper.Monitor, typeof( FakePartyFeatureDriver ), null, auto.Services, out var all, o => null ).ShouldBeTrue();
        all.ShouldHaveSingleItem().ShouldBeOfType<NotifyOption>();
    }

    [Test]
    public async Task an_option_is_created_with_the_services_of_its_owner_Async()
    {
        await using var auto = await CreateServicesAsync();
        var directory = auto.Services.GetRequiredService<PartyComponentOptionDirectory>();
        var compression = directory.GetOptions( typeof( FakePartyFeatureDriver ) ).Single( o => o.Name == "Compression" );
        compression.OptionType.ShouldBe( typeof( CompressionOption ) );
        var o = compression.Create( auto.Services, null ).ShouldBeOfType<CompressionOption>();
        o.Service.ShouldBeSameAs( auto.Services.GetRequiredService<FakeService>() );
        // The IConfiguration parameter of its constructor is its configuration.
        var main = directory.GetOptions( typeof( FakePartyFeatureDriver ) ).Single( o => o.Name == "Main" );
        main.Create( auto.Services, null ).ShouldBeOfType<MainOption>().Level.ShouldBe( 1 );
        main.Create( auto.Services, Section( """{ "Level": 3 }""" ) ).ShouldBeOfType<MainOption>().Level.ShouldBe( 3 );
    }

    [Test]
    public async Task the_options_are_created_with_their_configuration_and_an_invalid_one_is_an_error_Async()
    {
        await using var auto = await CreateServicesAsync();
        var directory = auto.Services.GetRequiredService<PartyComponentOptionDirectory>();
        directory.TryCreate( TestHelper.Monitor, typeof( FakePartyFeatureDriver ), Section( """{ "Main": { "Level": 3 } }""" ), auto.Services, out var created ).ShouldBeTrue();
        created.Select( o => o.GetType() ).ShouldBe( [typeof( HeaderOption ), typeof( MainOption ), typeof( CompressionOption )] );
        created.OfType<MainOption>().Single().Level.ShouldBe( 3 );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            directory.TryCreate( TestHelper.Monitor, typeof( FakePartyFeatureDriver ), Section( """{ "Main": { "Level": "high" } }""" ), auto.Services, out created ).ShouldBeFalse();
            created.ShouldBeEmpty();
            texts.ShouldContain( t => t.StartsWith( "Unable to create the option 'Owner:Main': Level must be an integer, not 'high'." ) );
        }
    }

    [Test]
    public async Task a_feature_driver_holds_the_options_of_each_party_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( _model );
        var r = await configuration.RunSuccessfullyAsync();
        await using var auto = r.LoadMap().CreateAutomaticServices( services =>
        {
            services.AddSingleton<FakeService>();
            services.AddSingleton( ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["StoreRootPath"] = TestHelper.TestProjectFolder.AppendPart( "OptionsStore" );
                c["FullName"] = "Test/$OptionsOwner";
                // Each option is looked up in the party's section, then in the "Defaults" above it.
                c["Defaults:FakeParty:Main:Level"] = "5";
                c["Local:FakeParty:Header"] = "false";
            } ).ShouldNotBeNull() );
        } );
        var party = auto.Services.GetRequiredService<ApplicationIdentityService>();
        await ((IHostedService)party).StartAsync( default );
        await party.InitializationTask;
        var driver = auto.Services.GetRequiredService<FakePartyFeatureDriver>();
        driver.GetOptions( party ).Select( o => o.GetType() ).ShouldBe( [typeof( MainOption ), typeof( CompressionOption )] );
        driver.GetOption<MainOption>( party ).ShouldNotBeNull().Level.ShouldBe( 5 );
        driver.GetOption<HeaderOption>( party ).ShouldBeNull();
        await ((IHostedService)party).StopAsync( default );
    }

    [Test]
    public async Task the_model_of_an_option_is_read_key_by_key_along_the_levels_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( _model );
        configuration.FirstBinPath.Types.Add( typeof( ModeledOption ) );
        var r = await configuration.RunSuccessfullyAsync();
        await using var auto = r.LoadMap().CreateAutomaticServices( services =>
        {
            services.AddSingleton<FakeService>();
            services.AddSingleton( ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["StoreRootPath"] = TestHelper.TestProjectFolder.AppendPart( "OptionsStore" );
                c["FullName"] = "Test/$OptionsOwner";
                // The party's section enables the option and sets Count, the "Defaults" give its Name.
                c["Defaults:FakeParty:Modeled:Name"] = "FromDefaults";
                c["Defaults:FakeParty:Modeled:Count"] = "2";
                c["Local:FakeParty:Modeled:Count"] = "4";
            } ).ShouldNotBeNull() );
        } );
        var party = auto.Services.GetRequiredService<ApplicationIdentityService>();
        await ((IHostedService)party).StartAsync( default );
        await party.InitializationTask;
        var option = auto.Services.GetRequiredService<FakePartyFeatureDriver>().GetOption<ModeledOption>( party ).ShouldNotBeNull();
        option.Configuration.Name.ShouldBe( "FromDefaults" );
        option.Configuration.Count.ShouldBe( 4 );
        await ((IHostedService)party).StopAsync( default );
    }

    // The selection rules (ConfigurationOptionSelector) are tested in CK.Configuration.Tests.
    static ImmutableConfigurationSection Section( string json )
        => new ImmutableConfigurationSection( new MutableConfigurationSection( "Owner" ).AddJson( json ) );

    static async Task CheckEngineErrorAsync( string expected, params Type[] options )
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ), typeof( ApplicationIdentityService ), typeof( FakePartyFeatureDriver ), typeof( FakeClientFeature ) );
        configuration.FirstBinPath.Types.Add( options );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            var r = await configuration.RunAsync();
            r.Status.ShouldBe( RunStatus.Failed );
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public async Task engine_errors_Async()
    {
        await CheckEngineErrorAsync( "The option 'CK.AppIdentity.Engine.Tests.Bad.NoAttributeOption' must be decorated with the attribute of its kind of owner",
                                     typeof( Bad.NoAttributeOption ) );
        await CheckEngineErrorAsync( "its attribute [PartyClientFeatureOption] is the one of another kind of owner.",
                                     typeof( Bad.WrongKindOption ) );
        await CheckEngineErrorAsync( "The option 'CK.AppIdentity.Engine.Tests.Bad.TwoConstructorsOption' must have exactly one public constructor",
                                     typeof( Bad.TwoConstructorsOption ) );
        await CheckEngineErrorAsync( "have the same name 'Main'.",
                                     typeof( MainOption ), typeof( Bad.MainOption ) );
        await CheckEngineErrorAsync( "has more than one IConfiguration parameter: an option has one configuration.",
                                     typeof( Bad.TwoConfigurationsOption ) );
        await CheckEngineErrorAsync( "the configuration of an option is a plain IConfiguration.",
                                     typeof( Bad.SectionOption ) );
    }
}
