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

        PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, options, Section( """{ "Tracing": true }""" ), out var selected ).ShouldBeTrue();
        selected.Select( s => s.Info.Name ).ShouldBe( ["Tracing"] );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, options, Section( """{ "Main": true, "Tracing": false }""" ), out _ ).ShouldBeFalse();
            texts.ShouldContain( "The option 'Main' requires the option 'Tracing' that is disabled." );
        }
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

    // The selection is a pure function of the options and the configuration: it is tested on hand made options.
    static readonly PartyComponentOptionInfo[] _options =
    [
        new PartyComponentOptionInfo( "Header", typeof( HeaderOption ), true, [], (_, _) => new HeaderOption() ),
        new PartyComponentOptionInfo( "Main", typeof( MainOption ), false, [], (_, c) => new MainOption( c ) ),
        new PartyComponentOptionInfo( "Compression", typeof( CompressionOption ), true, ["Main"], (sp, _) => new CompressionOption( new FakeService() ) ),
    ];

    static ImmutableConfigurationSection Section( string json )
        => new ImmutableConfigurationSection( new MutableConfigurationSection( "Owner" ).AddJson( json ) );

    static string[] Select( ImmutableConfigurationSection? section )
    {
        PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, _options, section, out var selected ).ShouldBeTrue();
        return selected.Select( s => s.Info.Name ).ToArray();
    }

    [Test]
    public void selection_defaults()
    {
        // Compression is enabled by default but Main is disabled: Compression is disabled with it.
        Select( null ).ShouldBe( ["Header"] );
        Select( Section( "{}" ) ).ShouldBe( ["Header"] );
    }

    [Test]
    public void selection_true_false_and_section()
    {
        Select( Section( """{ "Main": true }""" ) ).ShouldBe( ["Header", "Main", "Compression"] );
        Select( Section( """{ "Main": "True", "Header": false, "Compression": "false" }""" ) ).ShouldBe( ["Main"] );

        PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, _options, Section( """{ "Main": { "Level": 3 } }""" ), out var selected ).ShouldBeTrue();
        selected.Select( s => s.Info.Name ).ShouldBe( ["Header", "Main", "Compression"] );
        selected[0].Configuration.ShouldBeNull();
        selected[1].Configuration.ShouldNotBeNull()["Level"].ShouldBe( "3" );
        selected[2].Configuration.ShouldBeNull();
    }

    [Test]
    public void selection_errors()
    {
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, _options, Section( """{ "Main": "yes" }""" ), out var selected ).ShouldBeFalse();
            selected.ShouldBeEmpty();
            texts.ShouldContain( "Invalid option 'Owner:Main' = 'yes': true, false or a section is expected." );
        }
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, _options, Section( """{ "Compression": true }""" ), out var selected ).ShouldBeFalse();
            texts.ShouldContain( "The option 'Compression' requires the option 'Main' that is disabled." );
        }
    }

    [Test]
    public void selection_cascade_of_defaults()
    {
        var options = new[]
        {
            new PartyComponentOptionInfo( "A", typeof( object ), false, [], (_, _) => new object() ),
            new PartyComponentOptionInfo( "B", typeof( object ), true, ["A"], (_, _) => new object() ),
            new PartyComponentOptionInfo( "C", typeof( object ), true, ["B"], (_, _) => new object() ),
        };
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, options, (ImmutableConfigurationSection?)null, out var selected ).ShouldBeTrue();
            selected.ShouldBeEmpty();
            texts.ShouldContain( "The option 'B' (enabled by default) is disabled: it requires the option 'A' that is disabled." );
            texts.ShouldContain( "The option 'C' (enabled by default) is disabled: it requires the option 'B' that is disabled." );
        }
        PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, options, Section( """{ "A": true }""" ), out var all ).ShouldBeTrue();
        all.Select( s => s.Info.Name ).ShouldBe( ["A", "B", "C"] );
    }

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
        await CheckEngineErrorAsync( "its attribute [PartyClientOption] is the one of another kind of owner.",
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
