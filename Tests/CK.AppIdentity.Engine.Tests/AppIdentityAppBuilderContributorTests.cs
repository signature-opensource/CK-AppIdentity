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

    const string FakeType = "CK.AppIdentity.Engine.Tests.Fake.FakeClientFeature, CK.AppIdentity.Engine.Tests";
    const string OtherType = "CK.AppIdentity.Engine.Tests.Fake.FakeService, CK.AppIdentity.Engine.Tests";

    [Test]
    public void the_declared_clients_are_exposed_with_their_features_and_their_type_set()
    {
        var context = CreateContext( $$"""
            {
              "FullName": "Sample/$Front",
              "Local": {
                "ExcludedTypes": [ "{{OtherType}}" ],
                "PartyClient": {
                  "App": { "Types": [ "{{FakeType}}" ], "NgApp": { "ActiveCultures": "fr" }, "Hosting": { "RequestPath": "/" } },
                  "Tool": { "Types": [ { "Name": "{{FakeType}}", "Optional": true } ], "Native": { "Store": "Play" } }
                }
              },
              "Parties": [ { "FullName": "Tenant/$Tenant", "Local": { "Types": [ "{{OtherType}}" ] } } ]
            }
            """ );
        var c = new AppIdentityAppBuilderContributor();
        c.Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        c.Clients.Select( x => x.Name ).ShouldBe( ["App", "Tool"] );
        c.Clients[0].Features.Keys.ShouldBe( ["NgApp", "Hosting"], ignoreOrder: true );
        c.Clients[1].Features.Keys.ShouldBe( ["Native"] );
        // The type sets: the parties' and the clients' (the exclusions of the party apply to its clients).
        var binPath = context.Apps[0].BinPathConfiguration;
        binPath.TypeSets.Select( s => s.Name ).ShouldBe( ["$Front", "$Tenant", "App", "Tool"], ignoreOrder: true );
        binPath.FindTypeSet( "App" ).ShouldNotBeNull().Types.ShouldBe( [typeof( Fake.FakeClientFeature )] );
        binPath.FindTypeSet( "App" )!.ExcludedTypes.ShouldBe( [typeof( Fake.FakeService )] );
        binPath.FindTypeSet( "$Tenant" ).ShouldNotBeNull().Types.ShouldBe( [typeof( Fake.FakeService )] );
        context.Apps[0].CompileKeys.ShouldBe( ["Local:ExcludedTypes", "Local:PartyClient:App:Types", "Local:PartyClient:Tool:Types", "Parties:0:Local:Types"], ignoreOrder: true );

        // A client that no contributor builds is an error, and a client has one build feature.
        c.Clients[0].SetHandled( "NgApp" ).ShouldBeTrue();
        c.Clients[0].SetHandled( "ViteApp" ).ShouldBeFalse();
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.OnContributed( TestHelper.Monitor, context ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "No package builds the client 'Tool' of App 'Front'" ) );
        }
        c.Clients[1].SetHandled( "NgApp" );
        c.OnContributed( TestHelper.Monitor, context ).ShouldBeTrue();
    }
    [Test]
    public void the_keys_of_the_party_are_projected_in_CK_AppIdentity()
    {
        var context = CreateContext( """
            {
              "FullName": "Sample/$Front",
              "Defaults": { "ListeningAddress": [ "tcp:127.0.0.1:37300" ] },
              "Local": { "HttpEndpoint": true },
              "Parties": [ { "PartyName": "$Back" } ],
              "Logging": { "LogLevel": { "Default": "Warning" } }
            }
            """ );
        new AppIdentityAppBuilderContributor().Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        context.Apps[0].ProjectedSections.ShouldBe( new[] { "FullName", "Defaults", "Local", "Parties" }.ToDictionary( k => k, _ => "CK-AppIdentity" ), ignoreOrder: true );
    }

    [TestCase( """{ "FullName": "Sample/$Front", "Local": { "Types": [ "No.Such.Type, No.Such.Assembly" ] } }""",
               "Unable to find the type 'No.Such.Type, No.Such.Assembly' of \"ckonf:Apps:Front:Local:Types:0\"" )]
    [TestCase( """{ "FullName": "Sample/$Front", "Local": { "ExcludedTypes": [ "CK.AppIdentity.Engine.Tests.Fake.FakeService, CK.AppIdentity.Engine.Tests" ], "PartyClient": { "App": { "Types": [ "CK.AppIdentity.Engine.Tests.Fake.FakeService, CK.AppIdentity.Engine.Tests" ] } } } }""",
               "The client 'App' of App 'Front' adds the type" )]
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
