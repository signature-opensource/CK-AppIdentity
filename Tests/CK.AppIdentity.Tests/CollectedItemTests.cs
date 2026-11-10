using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class CollectedItemTests
{
    static KeyValuePair<string, IEnumerable<CollectedItem>> Named( string name, params CollectedItem[] items ) => new( name, items );

    [Test]
    public void an_item_has_a_value_and_a_path_from_its_holder()
    {
        var i = new CollectedItem( "{}", ["A", "B", "C"] );
        i.Holder.ShouldBe( "A" );
        i.Announcer.ShouldBe( "C" );
        i.Length.ShouldBe( 3 );
        i.ToString().ShouldBe( "A > B > C: {}" );
        var own = CollectedItem.Own( "{}", "A" );
        own.Holder.ShouldBe( "A" );
        own.Announcer.ShouldBe( "A" );
        own.IsSameItem( i ).ShouldBeTrue( "The same holder and value, whatever the path." );
        own.ShouldNotBe( i );

        Should.Throw<ArgumentException>( () => new CollectedItem( "", ["A"] ) );
        Should.Throw<ArgumentException>( () => new CollectedItem( new string( 'x', CollectedItem.MaxValueLength + 1 ), ["A"] ) );
        new CollectedItem( new string( 'x', CollectedItem.MaxValueLength ), ["A"] ).Value.Length.ShouldBe( CollectedItem.MaxValueLength );
        Should.Throw<ArgumentException>( () => new CollectedItem( "V", [] ) );
        Should.Throw<ArgumentException>( () => new CollectedItem( "V", ["A", "B", "A"] ) );
        Should.Throw<ArgumentException>( () => new CollectedItem( "V", Enumerable.Range( 0, PartyRoute.MaxLength + 1 ).Select( n => $"P{n}" ) ) );
        CollectedItem.TryCreate( "V", ["A", " "], out _, out var error ).ShouldBeFalse();
        error.ShouldContain( "distinct, non empty" );
    }

    [Test]
    public void a_received_item_is_accepted_then_extended()
    {
        var fromB = new CollectedItem( "V", ["A", "B"] );
        fromB.TryAccept( "B", "C", out var reason ).ShouldBeTrue( reason );
        fromB.Extend( "C" ).ShouldBe( new CollectedItem( "V", ["A", "B", "C"] ) );

        fromB.TryAccept( "X", "C", out reason ).ShouldBeFalse( "A remote only vouches for its own step." );
        reason.ShouldContain( "not the announcing remote" );
        fromB.TryAccept( "B", "A", out reason ).ShouldBeFalse( "No loop." );
        reason.ShouldContain( "a loop" );
        var full = new CollectedItem( "V", Enumerable.Range( 0, PartyRoute.MaxLength ).Select( n => $"P{n}" ) );
        full.TryAccept( $"P{PartyRoute.MaxLength - 1}", "C", out _ ).ShouldBeFalse( "It can't be extended." );
    }

    [Test]
    public void the_collected_items_are_sorted_bounded_and_distinct()
    {
        var b = new CollectedItem( "V", ["B", "H"] );
        var a2 = new CollectedItem( "W", ["A", "H"] );
        var a1 = new CollectedItem( "V", ["A", "X", "H"] );
        var d = new PartyDescription( null, default, collected: [Named( "Enlistment", b, a2, a1 ), Named( "Empty" )] );
        d.Collected.Keys.ShouldBe( ["Enlistment"], "A route name without item is not kept." );
        d.Collected["Enlistment"].ShouldBe( [a1, a2, b], "Sorted by holder, then value." );
        d.ToString().ShouldContain( "(Collected: Enlistment: 3)" );

        d.ShouldBe( new PartyDescription( null, default, collected: [Named( "Enlistment", a1, b, a2 )] ), "Compared item by item." );
        d.ShouldNotBe( new PartyDescription( null, default, collected: [Named( "Enlistment", a1, b )] ) );
        d.ShouldNotBe( new PartyDescription( null, default, collected: [Named( "Enlistment", a1, a2, new CollectedItem( "V", ["B", "Y", "H"] ) )] ),
                       "The paths are compared." );
        d.WithRoles( ["R"] ).Collected["Enlistment"].ShouldBe( d.Collected["Enlistment"] );

        Should.Throw<ArgumentException>( () => new PartyDescription( null, default, collected: [Named( "Enlistment", a1, new CollectedItem( "V", ["A"] ) )] ),
                                         "The same item twice." );
        Should.Throw<ArgumentException>( () => new PartyDescription( null, default, collected: [Named( "Bad name", a1 )] ) );
        var tooMany = Enumerable.Range( 0, CollectedItem.MaxCount + 1 ).Select( n => CollectedItem.Own( $"V{n}", "A" ) ).ToArray();
        Should.Throw<ArgumentException>( () => new PartyDescription( null, default, collected: [Named( "Enlistment", tooMany )] ) );
        new PartyDescription( null, default, collected: [Named( "Enlistment", tooMany[..^1] )] ).Collected["Enlistment"].Length.ShouldBe( CollectedItem.MaxCount );
    }

    [Test]
    public void the_collected_items_are_not_persisted()
    {
        var d = new PartyDescription( null, default, collected: [Named( "Enlistment", CollectedItem.Own( "V", "A" ) )] );
        using var stream = new MemoryStream();
        using( var w = new System.Text.Json.Utf8JsonWriter( stream ) ) d.Write( w );
        using var doc = System.Text.Json.JsonDocument.Parse( stream.ToArray() );
        PartyDescription.TryRead( doc.RootElement, out var back, out _ ).ShouldBeTrue();
        back.Collected.ShouldBeEmpty( "An item counts only from a connected remote: it is not persisted." );
    }

    [Test]
    public void the_builders_collect_the_own_items_then_the_derived_ones()
    {
        var facts = new PartyDescription.Builder();
        facts.Collect( "Enlistment", "V" );
        facts.Collect( "Enlistment", "V" );
        facts.Collect( "Enlistment", "W" );
        facts.Collected["Enlistment"].ShouldBe( ["V", "W"], "The same value twice is ignored." );
        Should.Throw<ArgumentException>( () => facts.Collect( "Enlistment", "" ) );

        var derived = new PartyDescription.DerivedBuilder( facts );
        derived.OwnCollected["Enlistment"].ShouldBe( ["V", "W"] );
        derived.Build().Collected.ShouldBeEmpty( "Nothing is collected unless the second phase sets it." );
        derived.SetCollected( "Enlistment", derived.OwnCollected["Enlistment"].Select( v => CollectedItem.Own( v, "A" ) ) );
        derived.Build().Collected["Enlistment"].Select( i => i.Value ).ShouldBe( ["V", "W"] );
        derived.SetCollected( "Enlistment", null );
        derived.Build().Collected.ShouldBeEmpty();
    }
}
