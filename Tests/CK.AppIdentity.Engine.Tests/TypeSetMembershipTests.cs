using CK.AppIdentity.Engine.Tests.Fake;
using CK.Core;
using CK.Setup;
using CK.Testing;
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
}
