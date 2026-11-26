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
          "AppContexts": {
            "Auth3": {
              "RootPath": "Auth3",
              "App": {
                "Defaults": { "Transport": { "ListeningAddress": [ "tcp:127.0.0.1:37300" ] } },
                "Local": { "AuthCenter": {} }
              }
            },
            "Front": { "RootPath": "Front", "App": { "Local": { "HttpEndpoint": { "Urls": "http://localhost:5300" } } } },
            "Worker": {
              "RootPath": "Worker",
              "App": {
                "Parties": [ { "PartyName": "$Outside", "Address": "tcp:10.0.0.1:1234" } ]
              }
            },
            "Elsewhere": { "Apps": [ { "Name": "Elsewhere.$Other", "FullName": "Sample/Auth3/Elsewhere/$Other/#Prod" } ] }
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
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), json ).ShouldNotBeNull();
        foreach( var c in central.AppContexts ) Directory.CreateDirectory( c.BuilderFolder );
        return (central, root);
    }

    static ImmutableConfigurationSection Project( CentralConfiguration central, string appContext )
    {
        var c = central.FindAppContext( appContext ).ShouldNotBeNull();
        central.WriteProjection( TestHelper.Monitor, c, [new AppIdentityAppBuilderContributor()] ).ShouldBeTrue();
        // The root keys and the address of a link are declared by the [assembly: CentralConfigurationKey] of the engine.
        central.PerDeploymentKeys.Select( k => k.Key ).ShouldContain( "Links:[*]:Link:Address" );
        return ImmutableConfigurationSection.CreateFromJson( "ckonf", File.ReadAllText( c.BuilderFolder.AppendPart( "ckonf.json" ) ) );
    }

    static void ShouldFail( CentralConfiguration central, string expected )
    {
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, central.AppContexts[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public void the_FullName_is_derived_and_the_links_are_the_remote_entries()
    {
        var (central, root) = Create( _graph );

        var front = Project( central, "Front" );
        front.GetRequiredSection( "App" ).GetChildren().Select( c => c.Key ).ShouldBe( ["FullName", "Local", "AppProject", "HostProject", "Parties"] );
        front["App:FullName"].ShouldBe( "Sample/Auth3/$Front/#Dev" );
        front["App:AppProject"].ShouldBe( "../Auth3.Front.App" );
        front["App:Parties:0:PartyName"].ShouldBe( "$Auth3" );
        front["App:Parties:0:AuthCenterRemote"].ShouldBe( "True" );
        front["App:Parties:0:Address"].ShouldBe( "tcp:127.0.0.1:37300" );
        front["App:Parties:0:KeyManagement:AutoTrustKey"].ShouldBe( "Once" );
        front["App:Parties:1"].ShouldBeNull();

        // The links in their order; a link without content is a remote without configuration.
        var auth3 = Project( central, "Auth3" );
        auth3["App:Parties:0:PartyName"].ShouldBe( "$Front" );
        auth3["App:Parties:0:Address"].ShouldBeNull( "Auth3 doesn't initiate: no address." );
        auth3["App:Parties:1:PartyName"].ShouldBe( "$Worker" );
        auth3.GetRequiredSection( "App:Parties:1" ).GetChildren().Select( c => c.Key ).ShouldBe( ["PartyName"] );

        // The explicit remotes stay, the links follow; the JSON kinds are kept.
        var worker = Project( central, "Worker" );
        worker["App:Parties:0:PartyName"].ShouldBe( "$Outside" );
        worker["App:Parties:1:PartyName"].ShouldBe( "$Auth3" );
        File.ReadAllText( root.Combine( "Worker/Worker.Builder/ckonf.json" ) ).ShouldContain( "\"Retry\": 3" );

        // An explicit FullName is kept (it must be the party of the App in its domain).
        var elsewhere = Project( central, "Elsewhere" );
        elsewhere["Apps:0:FullName"].ShouldBe( "Sample/Auth3/Elsewhere/$Other/#Prod" );
        elsewhere["Apps:0:AppProject"].ShouldBe( "../Auth3.Elsewhere.Other.App" );
    }

    [Test]
    public void an_App_of_a_sub_domain_derives_its_FullName_from_its_segments()
    {
        var (central, _) = Create( """{ "Domain": "Sample", "AppContexts": { "Shops": { "Apps": [ { "Name": "Customers.$Acme" } ] } } }""" );
        Project( central, "Shops" )["Apps:0:FullName"].ShouldBe( "Sample/Customers/$Acme/#Dev" );
    }

    [Test]
    public void the_Environment_applies_to_the_derived_FullName()
    {
        var (central, _) = Create( """
            { "Domain": "Sample", "GlobalDomainPrefix": "Sample", "Environment": "#Staging", "AppContexts": { "Front": { "RootPath": "Front", "App": {} } } }
            """ );
        Project( central, "Front" )["App:FullName"].ShouldBe( "Sample/$Front/#Staging" );
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
        var (central, _) = Create( $$"""
            {
              "Domain": "Sample",
              "GlobalDomainPrefix": "Sample",
              "AppContexts": { "Front": { "App": {} }, "Auth3": { "App": {} } },
              "Links": [ {{links}} ]
            }
            """ );
        ShouldFail( central, expected );
    }

    [Test]
    public void a_remote_both_linked_and_explicit_is_an_error()
    {
        var (central, _) = Create( """
            {
              "AppContexts": {
                "Front": { "App": { "Parties": [ { "PartyName": "$Auth3" } ] } },
                "Auth3": { "App": {} }
              },
              "Links": [ { "From": "$Front", "To": "$Auth3" } ]
            }
            """ );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, central.AppContexts[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "is linked" ) && t.Contains( "it must be one or the other" ) );
        }
    }

    [TestCase( """ "Domain": "Not-a-domain-$", """, "Invalid \"Domain\"" )]
    [TestCase( """ "Environment": "Prod", """, "Invalid \"Environment\"" )]
    public void invalid_identity_keys_are_errors( string keys, string expected )
    {
        var json = $$"""{ {{keys}} "AppContexts": { "Front": { "App": {} } } }""";
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppIdentity.Engine.Tests", Guid.NewGuid().ToString( "N" ) );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            // An invalid domain is the central configuration's error; the environment is the contributor's.
            var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), json );
            if( central != null )
            {
                Directory.CreateDirectory( central.AppContexts[0].BuilderFolder );
                central.WriteProjection( TestHelper.Monitor, central.AppContexts[0], [new AppIdentityAppBuilderContributor()] ).ShouldBeFalse();
            }
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [TestCase( """{ "Domain": "Sample", "AppContexts": { "Front": { "App": { "FullName": "Sample/$Back" } } } }""",
               "must be its party '$Front' in its domain 'Sample'" )]
    [TestCase( """{ "Domain": "Sample/Auth3", "AppContexts": { "Other": { "App": { "FullName": "Sample/Elsewhere/$Other" } } } }""",
               "must be its party '$Other' in its domain 'Sample/Auth3'" )]
    [TestCase( """{ "Domain": "Sample", "AppContexts": { "X": { "Apps": [ { "Name": "Customers.$Acme", "FullName": "Sample/$Acme" } ] } } }""",
               "must be its party '$Acme' in its domain 'Sample/Customers'" )]
    public void the_name_of_an_App_is_its_party_in_its_domain( string json, string expected )
    {
        var (central, _) = Create( json );
        ShouldFail( central, expected );
    }

    // An Opened tenant template owns its domain prefix (its run-time tenants are named by their creator); in a fleet App, a
    // tenant template uses the placeholder of its App (each process of the fleet has its own tenants).
    const string _openedTenants = """{ "DomainName": "Sample/Customers/{Name}", "PartyName": "${Name}", "MaxInstanceCount": 3 }""";

    [TestCase( $$"""
                 "Auth3": { "App": { "Parties": [ {{_openedTenants}} ] } },
                 "Front": { "App": { "Parties": [ {{_openedTenants}} ] } }
                 """, "have the same domain prefix 'Sample/Customers'" )]
    [TestCase( $$"""
                 "Auth3": { "App": { "Parties": [ {{_openedTenants}} ] } },
                 "Front": { "App": { "Parties": [ { "FullName": "Sample/Customers/Acme/$Acme" } ] } }
                 """, "The domain 'Sample/Customers/Acme' of 'ckonf:AppContexts:Front:App:Parties:0' is under the domain prefix 'Sample/Customers'" )]
    [TestCase( $$"""
                 "Auth3": { "App": { "Parties": [ {{_openedTenants}} ] } },
                 "Other": { "Apps": [ { "Name": "Customers.X.$Other" } ] }
                 """, "The domain 'Sample/Customers/X'" )]
    [TestCase( $$"""
                 "Worker": { "App": { "InstanceCount": 2, "Parties": [ {{_openedTenants}} ] } }
                 """, "must use the placeholder of its App in its names (\"{Worker.Name}\" or \"{Worker.Index}\")" )]
    public void invalid_tenant_templates_are_errors( string appContexts, string expected )
    {
        var (central, _) = Create( $$"""{ "Domain": "Sample", "GlobalDomainPrefix": "Sample", "AppContexts": { {{appContexts}} } }""" );
        ShouldFail( central, expected );
    }

    [Test]
    public void a_tenant_template_of_a_fleet_uses_the_placeholder_of_its_App()
    {
        var (central, _) = Create( """
            {
              "Domain": "Sample",
              "GlobalDomainPrefix": "Sample",
              "AppContexts": {
                "Worker": {
                  "App": {
                    "InstanceCount": 2,
                    "Parties": [ { "DomainName": "Sample/Customers-{Worker.Name}/{Name}", "PartyName": "${Name}", "MaxInstanceCount": 3 } ]
                  }
                }
              }
            }
            """ );
        var worker = Project( central, "Worker" );
        worker["App:Parties:0:DomainName"].ShouldBe( "Sample/Customers-{Worker.Name}/{Name}" );
    }

    [Test]
    public void a_fleet_is_named_by_its_pattern_and_a_link_to_it_is_a_remote_template()
    {
        var (central, root) = Create( """
            {
              "Domain": "Sample",
              "GlobalDomainPrefix": "Sample",
              "AppContexts": {
                "Auth3": { "App": {} },
                "Worker": { "App": { "InstanceCount": 2, "MaxInstanceCount": 4 } },
                "Front": { "App": { "Instances": [ "Red", "Blue" ], "FullName": "Sample/$Front-{Name}-{Index}/#Dev" } }
              },
              "Links": [
                { "From": "$Worker-{Name}", "To": "$Auth3", "Link": { "Address": "tcp:127.0.0.1:37300" } },
                { "From": "$Auth3", "To": "$Worker-{Name}", "Link": { "Address": "tcp:127.0.0.1:{38000+Index}" } },
                { "From": "$Auth3", "To": "$Front-{Name}-{Index}" }
              ]
            }
            """ );
        var worker = Project( central, "Worker" );
        worker["App:FullName"].ShouldBe( "Sample/$Worker-{Name}/#Dev" );
        worker["App:InstanceCount"].ShouldBe( "2" );
        worker["App:Parties:0:PartyName"].ShouldBe( "$Auth3" );

        var auth3 = Project( central, "Auth3" );
        auth3["App:Parties:0:PartyName"].ShouldBe( "$Worker-{Name}" );
        auth3["App:Parties:0:InstanceCount"].ShouldBe( "2" );
        // A link to an Opened fleet is an Opened remote template.
        auth3["App:Parties:0:MaxInstanceCount"].ShouldBe( "4" );
        auth3["App:Parties:1:MaxInstanceCount"].ShouldBeNull();
        auth3["App:Parties:0:Address"].ShouldBe( "tcp:127.0.0.1:{38000+Index}" );
        auth3["App:Parties:1:PartyName"].ShouldBe( "$Front-{Name}-{Index}" );
        auth3["App:Parties:1:Instances:1"].ShouldBe( "Blue" );
        File.ReadAllText( root.Combine( "Auth3.Builder/ckonf.json" ) ).ShouldContain( "\"InstanceCount\": 2" );
    }
}
