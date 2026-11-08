using CK.AppIdentity.Engine.Tests.Fake;
using CK.Core;
using CK.Setup;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    static readonly Type[] _model = [typeof( PartyComponentOptionDirectory ), typeof( ApplicationIdentityService ),
                                     typeof( FakeFeatureDriver ), typeof( MainOption ), typeof( HeaderOption ), typeof( CompressionOption ),
                                     typeof( FakeClient ), typeof( FakeDerivedClient ), typeof( CachingOption ), typeof( DerivedOnlyOption )];

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

        directory.OwnerTypes.ShouldBe( [typeof( FakeDerivedClient ), typeof( FakeFeatureDriver ), typeof( FakeClient )], ignoreOrder: true );

        var driver = directory.GetOptions( typeof( FakeFeatureDriver ) );
        driver.Select( o => o.Name ).ShouldBe( ["Header", "Main", "Compression"] );
        driver.Select( o => o.IsEnabledByDefault ).ShouldBe( [true, false, true] );
        // The optional relation of Header is an order only. Compression requires Main.
        driver[0].Requires.ShouldBeEmpty();
        driver[1].Requires.ShouldBeEmpty();
        driver[2].Requires.ShouldBe( ["Main"] );

        // The options of a base owner type apply to the derived one.
        directory.GetOptions( typeof( FakeClient ) ).Select( o => o.Name ).ShouldBe( ["Caching"] );
        var derived = directory.GetOptions( typeof( FakeDerivedClient ) );
        derived.Select( o => o.Name ).ShouldBe( ["Caching", "DerivedOnly"] );
        derived[1].Requires.ShouldBe( ["Caching"] );

        directory.GetOptions( typeof( string ) ).ShouldBeEmpty();
        directory.IsOptionName( typeof( FakeFeatureDriver ), "compression" ).ShouldBeTrue();
        directory.IsOptionName( typeof( FakeClient ), "DerivedOnly" ).ShouldBeFalse();
    }

    [Test]
    public async Task an_option_is_created_with_the_services_of_its_owner_Async()
    {
        await using var auto = await CreateServicesAsync();
        var directory = auto.Services.GetRequiredService<PartyComponentOptionDirectory>();
        var compression = directory.GetOptions( typeof( FakeFeatureDriver ) ).Single( o => o.Name == "Compression" );
        compression.OptionType.ShouldBe( typeof( CompressionOption ) );
        var o = compression.Create( auto.Services ).ShouldBeOfType<CompressionOption>();
        o.Service.ShouldBeSameAs( auto.Services.GetRequiredService<FakeService>() );
        directory.GetOptions( typeof( FakeFeatureDriver ) ).Single( o => o.Name == "Main" ).Create( auto.Services ).ShouldBeOfType<MainOption>();
    }

    // The selection is a pure function of the options and the configuration: it is tested on hand made options.
    static readonly PartyComponentOptionInfo[] _options =
    [
        new PartyComponentOptionInfo( "Header", typeof( HeaderOption ), true, [], _ => new HeaderOption() ),
        new PartyComponentOptionInfo( "Main", typeof( MainOption ), false, [], _ => new MainOption() ),
        new PartyComponentOptionInfo( "Compression", typeof( CompressionOption ), true, ["Main"], sp => new CompressionOption( new FakeService() ) ),
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
            new PartyComponentOptionInfo( "A", typeof( object ), false, [], _ => new object() ),
            new PartyComponentOptionInfo( "B", typeof( object ), true, ["A"], _ => new object() ),
            new PartyComponentOptionInfo( "C", typeof( object ), true, ["B"], _ => new object() ),
        };
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            PartyComponentOptionDirectory.TrySelect( TestHelper.Monitor, options, null, out var selected ).ShouldBeTrue();
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
        configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ), typeof( ApplicationIdentityService ), typeof( FakeFeatureDriver ), typeof( FakeClient ) );
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
    }
}
