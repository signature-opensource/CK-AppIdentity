using CK.AppIdentity.Engine.Tests.Fake;
using CK.Core;
using CK.Setup;
using CK.Testing;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests;

/// <summary>
/// The generated <see cref="PartyComponentDirectory"/> knows the party components that are members of each type set of
/// the BinPath: the types drive, a component that is not a member of the set of a party (or of a client) doesn't apply to it.
/// </summary>
[TestFixture]
public class TypeSetMembershipTests
{
    [Test]
    public async Task the_directory_knows_the_members_of_the_type_sets_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ),
                                              typeof( PartyComponentDirectory ),
                                              typeof( ApplicationIdentityService ),
                                              typeof( PartyClientFeatureDriver ),
                                              typeof( FakePartyFeatureDriver ),
                                              typeof( FakeClientFeatureDriver ),
                                              typeof( FakeClientFeature ),
                                              typeof( BuildClientFeature ),
                                              typeof( BuildClientFeatureDriver ) );
        // The party's set is not restricted: it has the whole playground. Its client "A" is restricted to BuildClientFeature.
        configuration.FirstBinPath.AddTypeSet( new TypeSetConfiguration( "$Party" ) );
        var client = new TypeSetConfiguration( "A" ) { Parent = "$Party" };
        client.Types.Add( typeof( BuildClientFeature ) );
        configuration.FirstBinPath.AddTypeSet( client );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        await using var services = map.CreateAutomaticServices( s => s.AddSingleton( ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Test/$Party" ).ShouldNotBeNull() ) );
        var d = services.Services.GetRequiredService<PartyComponentDirectory>();
        d.HasTypeSet( "$Party" ).ShouldBeTrue();
        d.HasTypeSet( "a" ).ShouldBeTrue( "Case insensitive." );
        d.IsMember( "$Party", typeof( FakePartyFeatureDriver ) ).ShouldBeTrue();
        d.IsMember( "$Party", typeof( FakeClientFeature ) ).ShouldBeTrue();
        d.IsMember( "A", typeof( BuildClientFeature ) ).ShouldBeTrue();
        d.IsMember( "A", typeof( FakeClientFeature ) ).ShouldBeFalse( "Not rooted in the restricted set of the client." );
        d.IsMember( "Unknown", typeof( FakeClientFeature ) ).ShouldBeTrue( "Without a set, all the components apply." );
    }

    // The engine runs on a BinPath whose host (its ProjectPath, under obj: ignored) has the projection of the party.
    static async Task<(RunStatus Status, string[] Texts)> RunWithProjectionAsync( string appsettings )
    {
        var host = TestHelper.TestProjectFolder.Combine( "obj/MembershipHost" );
        System.IO.Directory.CreateDirectory( host );
        System.IO.File.WriteAllText( host.AppendPart( "appsettings.json" ), appsettings );
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.ProjectPath = host;
        configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ),
                                              typeof( PartyComponentDirectory ),
                                              typeof( ApplicationIdentityService ),
                                              typeof( PartyClientFeatureDriver ),
                                              typeof( FakePartyFeatureDriver ),
                                              typeof( MainOption ),
                                              typeof( FakeClientFeatureDriver ),
                                              typeof( FakeClientFeature ),
                                              typeof( BuildClientFeature ),
                                              typeof( BuildClientFeatureDriver ) );
        // The party is restricted to FakeParty (its options are not rooted), its client "A" to Build.
        var party = new TypeSetConfiguration( "$Host" );
        party.Types.Add( typeof( FakePartyFeatureDriver ) );
        party.Types.Add( typeof( PartyClientFeatureDriver ) );
        configuration.FirstBinPath.AddTypeSet( party );
        var client = new TypeSetConfiguration( "A" ) { Parent = "$Host" };
        client.Types.Add( typeof( BuildClientFeature ) );
        configuration.FirstBinPath.AddTypeSet( client );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            var r = await configuration.RunAsync();
            return (r.Status, texts.ToArray());
        }
    }

    [Test]
    public async Task the_keys_of_the_projection_must_name_members_of_their_type_sets_Async()
    {
        (await RunWithProjectionAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "FakeParty": true, "PartyClient": { "A": { "Build": true } } } } }""" ))
            .Status.ShouldNotBe( RunStatus.Failed );

        var (status, texts) = await RunWithProjectionAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "FakeParty": { "Main": true } } } }""" );
        status.ShouldBe( RunStatus.Failed );
        texts.ShouldContain( t => t.StartsWith( "\"CK-AppIdentity:Local:FakeParty:Main\": the option 'CK.AppIdentity.Engine.Tests.Fake.MainOption' is not a member of the type set of '$Host'." ) );

        (status, texts) = await RunWithProjectionAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "PartyClient": { "A": { "Fake": true } } } } }""" );
        status.ShouldBe( RunStatus.Failed );
        texts.ShouldContain( t => t.StartsWith( "\"CK-AppIdentity:Local:PartyClient:A:Fake\": the client feature 'CK.AppIdentity.Engine.Tests.Fake.FakeClientFeature' is not a member of the type set of the client 'A'." ) );

        // false is fine: it doesn't use the feature.
        (await RunWithProjectionAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "FakeParty": { "Main": false }, "PartyClient": { "A": { "Build": true, "Fake": false } } } } }""" ))
            .Status.ShouldNotBe( RunStatus.Failed );
    }

    // The sets are not restricted (the playground): the scopes declared by the projection are checked.
    static async Task<(RunStatus Status, string[] Texts)> RunScopesAsync( string appsettings )
    {
        var host = TestHelper.TestProjectFolder.Combine( "obj/ScopeHost" );
        System.IO.Directory.CreateDirectory( host );
        System.IO.File.WriteAllText( host.AppendPart( "appsettings.json" ), appsettings );
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.ProjectPath = host;
        configuration.FirstBinPath.Types.Add( typeof( PartyComponentOptionDirectory ),
                                              typeof( PartyComponentDirectory ),
                                              typeof( ApplicationIdentityService ),
                                              typeof( PartyClientFeatureDriver ),
                                              typeof( FakePartyFeatureDriver ),
                                              typeof( BuildClientFeature ),
                                              typeof( BuildClientFeatureDriver ),
                                              typeof( OtherBuildClientFeature ),
                                              typeof( OtherBuildClientFeatureDriver ),
                                              typeof( NeedsFakeClientFeature ),
                                              typeof( NeedsFakeClientFeatureDriver ) );
        configuration.FirstBinPath.AddTypeSet( new TypeSetConfiguration( "$Host" ) );
        configuration.FirstBinPath.AddTypeSet( new TypeSetConfiguration( "A" ) { Parent = "$Host" } );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            var r = await configuration.RunAsync();
            return (r.Status, texts.ToArray());
        }
    }

    [Test]
    public async Task the_scopes_declared_by_the_projection_are_checked_Async()
    {
        // The set of the client has both builds (they are members): declaring one of them is fine.
        (await RunScopesAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "PartyClient": { "A": { "Build": true } } } } }""" ))
            .Status.ShouldNotBe( RunStatus.Failed );

        // Declaring both is ambiguous: IFakeBuild is single.
        var (status, texts) = await RunScopesAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "PartyClient": { "A": { "Build": true, "OtherBuild": true } } } } }""" );
        status.ShouldBe( RunStatus.Failed );
        texts.ShouldContain( t => t.StartsWith( "the client 'A' has more than one 'IFakeBuild'" ) );

        // NeedsFake requires the FakeParty feature of its party: the party must declare it.
        (status, texts) = await RunScopesAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "PartyClient": { "A": { "NeedsFake": true } } } } }""" );
        status.ShouldBe( RunStatus.Failed );
        texts.ShouldContain( t => t.StartsWith( "'NeedsFakeClientFeature' of the client 'A' requires 'FakePartyFeatureDriver' that the client 'A' doesn't have." ) );
        (await RunScopesAsync( """{ "CK-AppIdentity": { "FullName": "Test/$Host", "Local": { "FakeParty": true, "PartyClient": { "A": { "NeedsFake": true } } } } }""" ))
            .Status.ShouldNotBe( RunStatus.Failed );
    }
}
