using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class PartyRouteTests
{
    [Test]
    public void a_route_has_a_target_and_a_path_of_distinct_parties()
    {
        var r = new PartyRoute( "https://admin.acme.com/app-identity/enlist", ["A", "B", "C"] );
        r.Announcer.ShouldBe( "A" );
        r.NextHop.ShouldBe( "B" );
        r.Host.ShouldBe( "C" );
        r.Length.ShouldBe( 3 );
        r.ToString().ShouldBe( "A > B > C (https://admin.acme.com/app-identity/enlist)" );
        var hosted = PartyRoute.Hosted( "Some.Command", "C" );
        hosted.NextHop.ShouldBeNull();
        hosted.Announcer.ShouldBe( "C" );
        hosted.Host.ShouldBe( "C" );

        Should.Throw<ArgumentException>( () => new PartyRoute( "T", [] ) );
        Should.Throw<ArgumentException>( () => new PartyRoute( "T", ["A", "B", "A"] ) );
        Should.Throw<ArgumentException>( () => new PartyRoute( "T", ["A", " "] ) );
        Should.Throw<ArgumentException>( () => new PartyRoute( "T", Enumerable.Range( 0, PartyRoute.MaxLength + 1 ).Select( i => $"P{i}" ) ) );
    }

    [Test]
    public void a_route_is_compared_by_value()
    {
        new PartyRoute( "T", ["A", "B"] ).ShouldBe( new PartyRoute( "T", ["A", "B"] ) );
        new PartyRoute( "T", ["A", "B"] ).ShouldNotBe( new PartyRoute( "U", ["A", "B"] ) );
        new PartyRoute( "T", ["A", "B"] ).ShouldNotBe( new PartyRoute( "T", ["B", "A"] ) );
    }

    [Test]
    public void a_received_route_is_accepted_then_extended()
    {
        var fromB = new PartyRoute( "T", ["B", "C"] );
        fromB.TryAccept( "B", "A", out var reason ).ShouldBeTrue( reason );
        fromB.Extend( "A" ).ShouldBe( new PartyRoute( "T", ["A", "B", "C"] ) );

        fromB.TryAccept( "X", "A", out reason ).ShouldBeFalse( "A remote only vouches for its own next step." );
        reason.ShouldContain( "not the announcing remote" );
        new PartyRoute( "T", ["B", "A", "C"] ).TryAccept( "B", "A", out reason ).ShouldBeFalse( "No loop." );
        reason.ShouldContain( "a loop" );
        var full = new PartyRoute( "T", Enumerable.Range( 0, PartyRoute.MaxLength ).Select( i => $"P{i}" ) );
        full.TryAccept( "P0", "A", out reason ).ShouldBeFalse( "It can't be extended." );
    }

    [Test]
    public void the_best_route_is_the_shortest_then_the_ordinal_next_hop()
    {
        PartyRoute.SelectBest( [] ).ShouldBeNull();
        var viaC = new PartyRoute( "T", ["C", "H"] );
        var viaB = new PartyRoute( "T", ["B", "H"] );
        var longer = new PartyRoute( "T", ["A", "X", "H"] );
        PartyRoute.SelectBest( [longer, viaC, viaB] ).ShouldBeSameAs( viaB );
        PartyRoute.SelectBest( [viaC, longer] ).ShouldBeSameAs( viaC );
    }

    [Test]
    public void the_data_are_persisted_the_routes_are_not()
    {
        var d = new PartyDescription( null,
                                      default,
                                      null,
                                      [new KeyValuePair<string, string>( "Region", "eu-west" )],
                                      [new KeyValuePair<string, IEnumerable<PartyRoute>>( "Enlistment", [new PartyRoute( "T", ["A", "B"] )] ),
                                       new KeyValuePair<string, IEnumerable<PartyRoute>>( "Mail", [new PartyRoute( null, ["A", "M2"] ), new PartyRoute( null, ["A", "M1"] )] )] );
        d.Data["Region"].ShouldBe( "eu-west" );
        d.Routes["Enlistment"].ShouldHaveSingleItem().Host.ShouldBe( "B" );
        d.Routes["Mail"].Select( r => r.Host ).ShouldBe( ["M1", "M2"], "A name can have several hosts, sorted." );
        d.FindRoute( "Mail", "M2" ).ShouldNotBeNull().Target.ShouldBeNull();
        d.FindRoute( "Mail", "M3" ).ShouldBeNull();
        d.ToString().ShouldContain( "(Data: Region)" );
        d.ToString().ShouldContain( "(Routes: Enlistment: A > B (T), Mail: A > M1, Mail: A > M2)" );
        d.ShouldNotBe( new PartyDescription( null, default, null, d.Data ), "The routes are compared." );
        d.ShouldNotBe( new PartyDescription( null, default, null, null, d.Routes.Select( kv => new KeyValuePair<string, IEnumerable<PartyRoute>>( kv.Key, kv.Value ) ) ),
                       "The data are compared." );
        Should.Throw<ArgumentException>( () => new PartyDescription( null, default, null, [new KeyValuePair<string, string>( "Bad name", "x" )] ) );
        Should.Throw<ArgumentException>( () => new PartyDescription( null, default, null, null,
                                                                     [new KeyValuePair<string, IEnumerable<PartyRoute>>( "Mail", [new PartyRoute( null, ["A", "M1"] ), new PartyRoute( null, ["B", "M1"] )] )] ),
                                         "One route per host." );

        using var stream = new MemoryStream();
        using( var w = new System.Text.Json.Utf8JsonWriter( stream ) ) d.Write( w );
        using var doc = System.Text.Json.JsonDocument.Parse( stream.ToArray() );
        PartyDescription.TryRead( doc.RootElement, out var back, out _ ).ShouldBeTrue();
        back.Data.ShouldBe( d.Data );
        back.Routes.ShouldBeEmpty( "A route counts only from a connected remote: it is not persisted." );
    }
}
