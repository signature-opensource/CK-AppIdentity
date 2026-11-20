using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests;

[TestFixture]
public class CentralProjectionTests
{
    const string _graph = """
        {
          "Domain": "Sample/Auth3",
          "GlobalDomainPrefix": "Sample",
          "Builders": {
            "Auth3/Auth3.Auth3.Builder": {
              "App": {
                "Defaults": { "Transport": { "ListeningAddress": [ "tcp:127.0.0.1:37300" ] } },
                "Local": { "AuthCenter": {} }
              }
            },
            "Front/Auth3.Front.Builder": { "App": { "Local": { "HttpEndpoint": { "Urls": "http://localhost:5300" } } } },
            "Worker/Auth3.Worker.Builder": {
              "App": {
                "Parties": [ { "PartyName": "$Outside", "Address": "tcp:10.0.0.1:1234" } ]
              }
            },
            "Elsewhere.Other.Builder": { "App": { "FullName": "Sample/Elsewhere/$Other/#Prod" } }
          },
          "Links": [
            { "From": "$Front", "To": "$Auth3", "Link": { "AuthCenterRemote": true, "Address": "tcp:127.0.0.1:37300", "KeyManagement": { "AutoTrustKey": "Once" } } },
            { "From": "$Auth3", "To": "$Front", "Link": { "AuthCenterRemote": true, "KeyManagement": { "AutoTrustKey": "Once" } } },
            { "From": "$Worker", "To": "$Auth3", "Link": { "Address": "tcp:127.0.0.1:37300", "Retry": 3 } },
            { "From": "$Auth3", "To": "$Worker" }
          ]
        }
        """;

    static (CentralConfiguration Central, NormalizedPath Root) Create( string json )
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppIdentity.Engine.Tests", Guid.NewGuid().ToString( "N" ) );
        var builders = System.Text.Json.Nodes.JsonNode.Parse( json )?["Builders"]?.AsObject();
        if( builders != null )
        {
            foreach( var (b, _) in builders ) Directory.CreateDirectory( root.Combine( b ) );
        }
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), json ).ShouldNotBeNull();
        return (central, root);
    }

    static ImmutableConfigurationSection Project( CentralConfiguration central, NormalizedPath root, string builder )
    {
        var b = central.FindBuilder( root.Combine( builder ) ).ShouldNotBeNull();
        central.WriteProjection( TestHelper.Monitor, b, [new AppIdentityAppBuilderContributor()] ).ShouldBeTrue();
        return ImmutableConfigurationSection.CreateFromJson( "ckonf", File.ReadAllText( root.Combine( builder ).AppendPart( "ckonf.json" ) ) );
    }

    [Test]
    public void the_FullName_is_derived_and_the_links_are_the_remote_entries()
    {
        var (central, root) = Create( _graph );

        var front = Project( central, root, "Front/Auth3.Front.Builder" );
        front.GetRequiredSection( "App" ).GetChildren().Select( c => c.Key ).ShouldBe( ["FullName", "Local", "Parties"] );
        front["App:FullName"].ShouldBe( "Sample/Auth3/$Front/#Dev" );
        front["App:Parties:0:PartyName"].ShouldBe( "$Auth3" );
        front["App:Parties:0:AuthCenterRemote"].ShouldBe( "True" );
        front["App:Parties:0:Address"].ShouldBe( "tcp:127.0.0.1:37300" );
        front["App:Parties:0:KeyManagement:AutoTrustKey"].ShouldBe( "Once" );
        front["App:Parties:1"].ShouldBeNull();

        // The links in their order; a link without content is a remote without configuration.
        var auth3 = Project( central, root, "Auth3/Auth3.Auth3.Builder" );
        auth3["App:Parties:0:PartyName"].ShouldBe( "$Front" );
        auth3["App:Parties:0:Address"].ShouldBeNull( "Auth3 doesn't initiate: no address." );
        auth3["App:Parties:1:PartyName"].ShouldBe( "$Worker" );
        auth3.GetRequiredSection( "App:Parties:1" ).GetChildren().Select( c => c.Key ).ShouldBe( ["PartyName"] );

        // The explicit remotes stay, the links follow; the JSON kinds are kept.
        var worker = Project( central, root, "Worker/Auth3.Worker.Builder" );
        worker["App:Parties:0:PartyName"].ShouldBe( "$Outside" );
        worker["App:Parties:1:PartyName"].ShouldBe( "$Auth3" );
        File.ReadAllText( root.Combine( "Worker/Auth3.Worker.Builder/ckonf.json" ) ).ShouldContain( "\"Retry\": 3" );

        // An explicit FullName is kept (the name of its App is its identity).
        Project( central, root, "Elsewhere.Other.Builder" )["App:FullName"].ShouldBe( "Sample/Elsewhere/$Other/#Prod" );
    }

    [Test]
    public void the_Environment_applies_to_the_derived_FullName()
    {
        var (central, root) = Create( """
            { "Domain": "Sample", "GlobalDomainPrefix": "Sample", "Environment": "#Staging", "Builders": { "Front/Front.Builder": { "App": {} } } }
            """ );
        Project( central, root, "Front/Front.Builder" )["App:FullName"].ShouldBe( "Sample/$Front/#Staging" );
    }

    [TestCase( """{ "From": "$Front", "To": "$Nobody" }""", "is not a party of the central configuration" )]
    [TestCase( """{ "From": "Front", "To": "$Auth3" }""", "a party name (\"$Name\") is expected" )]
    [TestCase( """{ "From": "$Front", "To": "$Front" }""", "to itself" )]
    [TestCase( """{ "From": "$Front", "To": "$Auth3", "Link": { "PartyName": "$X" } }""", "can't have a \"PartyName\"" )]
    [TestCase( """{ "From": "$Front", "To": "$Auth3", "Link": true }""", "must be an object" )]
    [TestCase( """{ "From": "$Front", "To": "$Auth3", "Other": 1 }""", "Unknown key \"Links:0:Other\"" )]
    [TestCase( """{ "From": "$Front", "To": "$Auth3" }, { "From": "$front", "To": "$auth3" }""", "is defined twice" )]
    public void invalid_links_are_errors( string links, string expected )
    {
        var (central, root) = Create( $$"""
            {
              "Domain": "Sample",
              "GlobalDomainPrefix": "Sample",
              "Builders": { "Front/Front.Builder": { "App": {} }, "Auth3/Auth3.Builder": { "App": {} } },
              "Links": [ {{links}} ]
            }
            """ );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, central.Builders[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public void a_remote_both_linked_and_explicit_is_an_error()
    {
        var (central, root) = Create( """
            {
              "Builders": {
                "Front/Front.Builder": { "App": { "Parties": [ { "PartyName": "$Auth3" } ] } },
                "Auth3/Auth3.Builder": { "App": {} }
              },
              "Links": [ { "From": "$Front", "To": "$Auth3" } ]
            }
            """ );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, central.Builders[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "is linked" ) && t.Contains( "it must be one or the other" ) );
        }
    }

    [TestCase( """ "Domain": "Not a domain", """, "Invalid \"Domain\"" )]
    [TestCase( """ "Environment": "Prod", """, "Invalid \"Environment\"" )]
    public void invalid_identity_keys_are_errors( string keys, string expected )
    {
        var (central, root) = Create( $$"""{ {{keys}} "Builders": { "Front/Front.Builder": { "App": {} } } }""" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, central.Builders[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [TestCase( """{ "Domain": "Sample/Auth3", "Builders": { "Front/Front.Builder": { "App": {} } } }""",
               "must be the identity of its party: 'Sample.Auth3.Front'" )]
    [TestCase( """{ "Domain": "Sample/Auth3", "GlobalDomainPrefix": "Sample", "Builders": { "Front/Sample.Auth3.Front.Builder": { "App": {} } } }""",
               "must be the identity of its party: 'Auth3.Front'" )]
    [TestCase( """{ "Builders": { "Front/Sample.Front.Builder": { "App": {} } } }""",
               "must be the identity of its party: 'Front'" )]
    [TestCase( """{ "Domain": "Sample", "Builders": { "Sample.Front.Builder": { "App": { "FullName": "Sample/$Back" } } } }""",
               "must be the identity of its party: 'Sample.Back'" )]
    [TestCase( """{ "Domain": "Sample/Auth3", "GlobalDomainPrefix": "Sample", "Builders": { "Elsewhere.Other.Builder": { "App": { "FullName": "Elsewhere/$Other" } } } }""",
               "The domain 'Elsewhere' of the \"FullName\" of" )]
    [TestCase( """{ "GlobalDomainPrefix": "Sample", "Builders": { "Front/Front.Builder": { "App": {} } } }""",
               "requires a \"Domain\"" )]
    [TestCase( """{ "Domain": "Sample/Auth3", "GlobalDomainPrefix": "Sam", "Builders": { "Front/Front.Builder": { "App": {} } } }""",
               "it must be the \"Domain\" 'Sample/Auth3' or a prefix of it" )]
    public void the_name_of_an_App_is_the_identity_of_its_party( string json, string expected )
    {
        var (central, root) = Create( json );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, central.Builders[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public void the_name_of_an_App_is_its_domain_and_its_party()
    {
        var (central, root) = Create( """{ "Domain": "Sample/Auth3", "Builders": { "Front/Sample.Auth3.Front.Builder": { "App": {} } } }""" );
        Project( central, root, "Front/Sample.Auth3.Front.Builder" )["App:FullName"].ShouldBe( "Sample/Auth3/$Front/#Dev" );
    }

    // An Opened tenant template owns its domain prefix (its run-time tenants are named by their creator); in a fleet App, a
    // tenant template uses the placeholder of its App (each process of the fleet has its own tenants).
    const string _openedTenants = """{ "DomainName": "Sample/Customers/{Name}", "PartyName": "${Name}", "MaxInstanceCount": 3 }""";

    [TestCase( $$"""
                 "Auth3/Auth3.Builder": { "App": { "Parties": [ {{_openedTenants}} ] } },
                 "Front/Front.Builder": { "App": { "Parties": [ {{_openedTenants}} ] } }
                 """, "have the same domain prefix 'Sample/Customers'" )]
    [TestCase( $$"""
                 "Auth3/Auth3.Builder": { "App": { "Parties": [ {{_openedTenants}} ] } },
                 "Front/Front.Builder": { "App": { "Parties": [ { "FullName": "Sample/Customers/Acme/$Acme" } ] } }
                 """, "The domain 'Sample/Customers/Acme' of 'ckonf:Builders:Front/Front.Builder:App:Parties:0' is under the domain prefix 'Sample/Customers'" )]
    [TestCase( $$"""
                 "Auth3/Auth3.Builder": { "App": { "Parties": [ {{_openedTenants}} ] } },
                 "Customers.X.Other.Builder": { "App": { "FullName": "Sample/Customers/X/$Other" } }
                 """, "The domain 'Sample/Customers/X'" )]
    [TestCase( $$"""
                 "Worker/Worker.Builder": { "App": { "InstanceCount": 2, "Parties": [ {{_openedTenants}} ] } }
                 """, "must use the placeholder of its App in its names (\"{Worker.Name}\" or \"{Worker.Index}\")" )]
    public void invalid_tenant_templates_are_errors( string builders, string expected )
    {
        var (central, root) = Create( $$"""{ "Domain": "Sample", "GlobalDomainPrefix": "Sample", "Builders": { {{builders}} } }""" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, central.Builders[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public void a_tenant_template_of_a_fleet_uses_the_placeholder_of_its_App()
    {
        var (central, root) = Create( """
            {
              "Domain": "Sample",
              "GlobalDomainPrefix": "Sample",
              "Builders": {
                "Worker/Worker.Builder": {
                  "App": {
                    "InstanceCount": 2,
                    "Parties": [ { "DomainName": "Sample/Customers-{Worker.Name}/{Name}", "PartyName": "${Name}", "MaxInstanceCount": 3 } ]
                  }
                }
              }
            }
            """ );
        var worker = Project( central, root, "Worker/Worker.Builder" );
        worker["App:Parties:0:DomainName"].ShouldBe( "Sample/Customers-{Worker.Name}/{Name}" );
    }

    [Test]
    public void a_fleet_is_named_by_its_pattern_and_a_link_to_it_is_a_remote_template()
    {
        var (central, root) = Create( """
            {
              "Domain": "Sample",
              "GlobalDomainPrefix": "Sample",
              "Builders": {
                "Auth3/Auth3.Builder": { "App": {} },
                "Worker/Worker.Builder": { "App": { "InstanceCount": 2, "MaxInstanceCount": 4 } },
                "Front/Front.Builder": { "App": { "Instances": [ "Red", "Blue" ], "FullName": "Sample/$Front-{Name}-{Index}/#Dev" } }
              },
              "Links": [
                { "From": "$Worker-{Name}", "To": "$Auth3", "Link": { "Address": "tcp:127.0.0.1:37300" } },
                { "From": "$Auth3", "To": "$Worker-{Name}", "Link": { "Address": "tcp:127.0.0.1:{38000+Index}" } },
                { "From": "$Auth3", "To": "$Front-{Name}-{Index}" }
              ]
            }
            """ );
        var worker = Project( central, root, "Worker/Worker.Builder" );
        worker["App:FullName"].ShouldBe( "Sample/$Worker-{Name}/#Dev" );
        worker["App:InstanceCount"].ShouldBe( "2" );
        worker["App:Parties:0:PartyName"].ShouldBe( "$Auth3" );

        var auth3 = Project( central, root, "Auth3/Auth3.Builder" );
        auth3["App:Parties:0:PartyName"].ShouldBe( "$Worker-{Name}" );
        auth3["App:Parties:0:InstanceCount"].ShouldBe( "2" );
        // A link to an Opened fleet is an Opened remote template.
        auth3["App:Parties:0:MaxInstanceCount"].ShouldBe( "4" );
        auth3["App:Parties:1:MaxInstanceCount"].ShouldBeNull();
        auth3["App:Parties:0:Address"].ShouldBe( "tcp:127.0.0.1:{38000+Index}" );
        auth3["App:Parties:1:PartyName"].ShouldBe( "$Front-{Name}-{Index}" );
        auth3["App:Parties:1:Instances:1"].ShouldBe( "Blue" );
        File.ReadAllText( root.Combine( "Auth3/Auth3.Builder/ckonf.json" ) ).ShouldContain( "\"InstanceCount\": 2" );
    }
}
