using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
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
        // The clients are the anchors of the restrictions of the client features.
        context.Apps[0].Anchors.Where( a => a.Kinds.Contains( "Client" ) ).Select( a => a.Path ).ShouldBe( ["Local:PartyClient:App", "Local:PartyClient:Tool"], ignoreOrder: true );
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
    public void the_parties_are_the_anchors_of_the_restrictions()
    {
        var context = CreateContext( """
            {
              "FullName": "Sample/$Front",
              "Defaults": { "KeyManagement": { "AutoTrustKey": "Once" } },
              "Local": { "HttpEndpoint": true },
              "Parties": [
                { "PartyName": "$Back", "Address": "tcp:127.0.0.1:3712" },
                { "PartyName": "$Worker-{Name}", "MaxInstanceCount": 5 },
                { "PartyName": "$Cart-{Index}", "InstanceCount": 2 }
              ]
            }
            """ );
        new AppIdentityAppBuilderContributor().Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        var app = context.Apps[0];
        app.InitSections.ShouldBe( ["FullName", "Defaults", "Local", "Parties"], ignoreOrder: true );
        var anchors = app.Anchors.ToDictionary( a => a.Path );
        anchors.Keys.ShouldBe( ["Local", "Parties:0", "Parties:1", "Parties:2"], ignoreOrder: true );
        anchors["Local"].Kinds.ShouldBe( ["Party", "Local"], ignoreOrder: true );
        anchors["Parties:0"].Kinds.ShouldBe( ["Party", "Remote"], ignoreOrder: true );
        anchors["Parties:1"].Kinds.ShouldContain( "OpenedRemoteTemplate" );
        anchors["Parties:2"].Kinds.ShouldContain( "RemoteTemplate" );
        anchors["Parties:2"].Kinds.ShouldNotContain( "OpenedRemoteTemplate" );
        anchors["Parties:1"].Fallbacks.ShouldBe( ["Defaults"] );

        // What KeyManagement declares: the Opened template gets "Never" (the "Defaults" say "Once").
        var restriction = new PackageConfigurationLock( "KeyManagementFeatureDriver",
                                                        ["KeyManagement"],
                                                        new ConfigurationLockAttribute( "OpenedRemoteTemplate:AutoTrustKey", ConfigurationLevel.Compile ) { Value = "Never" } );
        AppBuilderConfiguration.ApplyLocks( TestHelper.Monitor, app, [], [restriction] ).ShouldBeTrue();
        app.BinPathConfiguration.CompiledValues.Count.ShouldBe( 1 );
        app.BinPathConfiguration.CompiledValues["CK-AppIdentity:Parties:1:KeyManagement:AutoTrustKey"].ShouldBe( "Never" );
        app.BurntKeys.ShouldBe( ["Parties:1:KeyManagement:AutoTrustKey"] );
    }

    [Test]
    public void the_sections_of_an_option_are_in_each_of_its_concrete_owners()
    {
        var c = new AppIdentityAppBuilderContributor();
        Type[] appTypes = [typeof( Fake.FakePartyFeatureDriver ), typeof( Fake.MainOption ),
                           typeof( Fake.FakeClientFeature ), typeof( Fake.FakeDerivedClientFeature ), typeof( Fake.CachingOption ), typeof( Fake.DerivedOnlyOption )];
        c.ResolveSections( typeof( Fake.MainOption ), appTypes ).ShouldBe( ["FakeParty:Main"] );
        // An option of a base owner applies to the derived owners.
        c.ResolveSections( typeof( Fake.CachingOption ), appTypes ).ShouldBe( ["Fake:Caching", "FakeDerived:Caching"], ignoreOrder: true );
        c.ResolveSections( typeof( Fake.DerivedOnlyOption ), appTypes ).ShouldBe( ["FakeDerived:DerivedOnly"] );
        // A feature is its configurable name (the default).
        c.ResolveSections( typeof( Fake.FakePartyFeatureDriver ), appTypes ).ShouldBeNull();
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

    [Test]
    public void a_fleet_App_is_checked_for_each_instance_and_its_instances_share_the_type_sets()
    {
        var context = CreateContext( """
            {
              "Instances": [ "Red", "Blue" ],
              "FullName": "Sample/$Trolley-{Name}/#Dev",
              "Local": { "HttpEndpoint": { "Urls": "http://localhost:{5400+Index}" } },
              "Parties": [ { "FullName": "Shop-{Name}/$Shop-{Name}", "InstanceCount": 2 } ]
            }
            """ );
        new AppIdentityAppBuilderContributor().Contribute( TestHelper.Monitor, context ).ShouldBeTrue();
        // The sets are named after the templates: an instance finds its set at run time ("$Trolley-Red").
        context.Apps[0].BinPathConfiguration.TypeSets.Select( s => s.Name ).ShouldBe( ["$Trolley-{Name}", "$Shop-{Name}"], ignoreOrder: true );
    }

    [TestCase( """{ "InstanceCount": 2, "FullName": "Sample/$Trolley/#Dev" }""", "must use the \"{Name}\" or \"{Index}\" placeholder" )]
    [TestCase( """{ "InstanceCount": 0, "FullName": "Sample/$Trolley-{Name}/#Dev" }""", "A positive integer is expected" )]
    public void invalid_fleets_are_build_errors( string json, string expected )
    {
        var context = CreateContext( json );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            new AppIdentityAppBuilderContributor().Contribute( TestHelper.Monitor, context ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }
}
