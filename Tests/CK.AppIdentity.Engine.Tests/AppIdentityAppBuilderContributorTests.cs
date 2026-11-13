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
    static AppBuilderContext CreateContext( string appJson )
    {
        var engine = new EngineConfiguration();
        var section = ImmutableConfigurationSection.CreateFromJson( "ckonf:Apps", "Front", appJson );
        var app = new AppConfiguration( "Front", TestHelper.TestProjectFolder, TestHelper.TestProjectFolder, section, engine.FirstBinPath );
        return new AppBuilderContext( engine, [app] );
    }

    [Test]
    public void the_declared_clients_are_exposed_and_their_Type_is_a_compile_key()
    {
        var context = CreateContext( """
            {
              "FullName": "Sample/$Front",
              "Local": { "PartyClient": { "App": { "Type": "TypeScript", "Kind": "DefaultApp" }, "Tool": { "Type": "Local" } } }
            }
            """ );
        var c = new AppIdentityAppBuilderContributor();
        c.Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        c.Clients.Select( x => (x.Name, x.Type) ).ShouldBe( [("App", "TypeScript"), ("Tool", "Local")] );
        c.Clients[0].Configuration["Kind"].ShouldBe( "DefaultApp" );
        context.Apps[0].CompileKeys.ShouldBe( ["Local:PartyClient:App:Type", "Local:PartyClient:Tool:Type"], ignoreOrder: true );

        // A client that no contributor handles is an error.
        c.Clients[0].SetHandled();
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.OnContributed( TestHelper.Monitor, context ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "No package builds the client 'Tool' of App 'Front' of Type 'Local'" ) );
        }
        c.Clients[1].SetHandled();
        c.OnContributed( TestHelper.Monitor, context ).ShouldBeTrue();
    }

    [Test]
    public void the_keys_of_the_party_are_projected_in_CK_AppIdentity()
    {
        var context = CreateContext( """
            {
              "FullName": "Sample/$Front",
              "Defaults": { "ListeningAddress": [ "tcp:127.0.0.1:37300" ] },
              "Local": { "AllowFeatures": "HttpEndpoint" },
              "Parties": [ { "PartyName": "$Back" } ],
              "Logging": { "LogLevel": { "Default": "Warning" } }
            }
            """ );
        new AppIdentityAppBuilderContributor().Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        context.Apps[0].ProjectedSections.ShouldBe( new[] { "FullName", "Defaults", "Local", "Parties" }.ToDictionary( k => k, _ => "CK-AppIdentity" ), ignoreOrder: true );
    }

    [TestCase( """{ "FullName": "Sample/$Front", "Local": { "PartyClient": { "App": { "Kind": "DefaultApp" } } } }""",
               "has no \"Type\"" )]
    [TestCase( """{ "FullName": "not a valid full name!" }""",
               "Invalid party in App 'Front'" )]
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
    public void an_App_without_the_keys_of_a_party_is_not_a_party()
    {
        var context = CreateContext( """{ "Logging": {} }""" );
        var c = new AppIdentityAppBuilderContributor();
        c.Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        c.Clients.ShouldBeEmpty();
        context.Apps[0].ProjectedSections.ShouldBeEmpty();
    }
}
