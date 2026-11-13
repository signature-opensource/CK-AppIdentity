using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests;

[TestFixture]
public class AppIdentityAppBuilderContributorTests
{
    static AppBuilderContext CreateContext( string binPathJson )
    {
        var engine = new EngineConfiguration();
        var section = ImmutableConfigurationSection.CreateFromJson( "ckonf:App:BinPaths", "Front", binPathJson );
        var binPath = new AppBinPath( "Front", TestHelper.TestProjectFolder, TestHelper.TestProjectFolder, section, engine.FirstBinPath );
        return new AppBuilderContext( engine, [binPath] );
    }

    [Test]
    public void the_declared_clients_are_exposed_and_their_Type_is_a_compile_key()
    {
        var context = CreateContext( """
            {
              "CK-AppIdentity": {
                "FullName": "Sample/$Front",
                "Local": { "PartyClient": { "App": { "Type": "TypeScript", "Kind": "DefaultApp" }, "Tool": { "Type": "Local" } } }
              }
            }
            """ );
        var c = new AppIdentityAppBuilderContributor();
        c.Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        c.Clients.Select( x => (x.Name, x.Type) ).ShouldBe( [("App", "TypeScript"), ("Tool", "Local")] );
        c.Clients[0].Configuration["Kind"].ShouldBe( "DefaultApp" );
        context.BinPaths[0].CompileKeys.ShouldBe( ["CK-AppIdentity:Local:PartyClient:App:Type", "CK-AppIdentity:Local:PartyClient:Tool:Type"], ignoreOrder: true );

        // A client that no contributor handles is an error.
        c.Clients[0].SetHandled();
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.OnContributed( TestHelper.Monitor, context ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "No package builds the client 'Tool' of BinPath 'Front' of Type 'Local'" ) );
        }
        c.Clients[1].SetHandled();
        c.OnContributed( TestHelper.Monitor, context ).ShouldBeTrue();
    }

    [TestCase( """{ "CK-AppIdentity": { "FullName": "Sample/$Front", "Local": { "PartyClient": { "App": { "Kind": "DefaultApp" } } } } }""",
               "has no \"Type\"" )]
    [TestCase( """{ "CK-AppIdentity": { "FullName": "not a valid full name!" } }""",
               "Invalid \"CK-AppIdentity\" in BinPath 'Front'" )]
    public void invalid_parties_are_build_errors( string json, string expected )
    {
        var context = CreateContext( json );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            new AppIdentityAppBuilderContributor().Contribute( TestHelper.Monitor, context ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public void a_BinPath_without_CK_AppIdentity_is_not_a_party()
    {
        var context = CreateContext( """{ "Logging": {} }""" );
        var c = new AppIdentityAppBuilderContributor();
        c.Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        c.Clients.ShouldBeEmpty();
    }
}
