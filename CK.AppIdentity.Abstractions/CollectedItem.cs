using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// An item collected along a route (see <see cref="PartyDescription.Collected"/>): a party states an item for a route
/// name, and the item travels hop by hop toward the host of the route, each party adding itself to its <see cref="Path"/>.
/// <para>
/// The <see cref="Value"/> is a mere string, never interpreted by the route: the party that states the item sets it (a
/// small JSON object, for instance) and the other parties pass it along unchanged. An item is identified by its
/// <see cref="Holder"/> and its <see cref="Value"/>.
/// </para>
/// </summary>
public sealed class CollectedItem : IEquatable<CollectedItem>
{
    /// <summary>
    /// The maximal length of a <see cref="Value"/>.
    /// </summary>
    public const int MaxValueLength = 1024;

    /// <summary>
    /// The maximal number of items of a route name in a description.
    /// </summary>
    public const int MaxCount = 64;

    /// <summary>
    /// Initializes a new item.
    /// </summary>
    /// <param name="value">The value: a non empty string of at most <see cref="MaxValueLength"/> characters.</param>
    /// <param name="path">
    /// The full names of the parties, from the holder to the announcing one (1 to <see cref="PartyRoute.MaxLength"/> distinct names).
    /// </param>
    public CollectedItem( string value, IEnumerable<string> path )
    {
        Throw.CheckNotNullArgument( path );
        var p = path.ToImmutableArray();
        if( !Check( value, p, out var error ) ) Throw.ArgumentException( error );
        Value = value;
        Path = p;
    }

    CollectedItem( string value, ImmutableArray<string> path )
    {
        Value = value;
        Path = path;
    }

    /// <summary>
    /// Tries to create an item (from data that can't be trusted).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="path">The path.</param>
    /// <param name="item">The item on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryCreate( string? value,
                                  IEnumerable<string>? path,
                                  [NotNullWhen( true )] out CollectedItem? item,
                                  [NotNullWhen( false )] out string? error )
    {
        item = null;
        var p = path?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
        if( !Check( value, p, out error ) ) return false;
        item = new CollectedItem( value, p );
        return true;
    }

    static bool Check( [NotNullWhen( true )] string? value, ImmutableArray<string> p, [NotNullWhen( false )] out string? error )
    {
        if( string.IsNullOrEmpty( value ) || value.Length > MaxValueLength )
        {
            error = $"A collected value is a non empty string of at most {MaxValueLength} characters.";
            return false;
        }
        if( p.Length < 1 || p.Length > PartyRoute.MaxLength )
        {
            error = $"A collected item's path has 1 to {PartyRoute.MaxLength} parties.";
            return false;
        }
        if( p.Any( string.IsNullOrWhiteSpace ) || p.Distinct( StringComparer.Ordinal ).Count() != p.Length )
        {
            error = "A collected item's path contains distinct, non empty full names.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Creates an item stated by a party: a path of one name.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="holderFullName">The full name of the party that states it.</param>
    /// <returns>The item.</returns>
    public static CollectedItem Own( string value, string holderFullName ) => new CollectedItem( value, [holderFullName] );

    /// <summary>
    /// Gets the value: a string set by the <see cref="Holder"/>.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the full names of the parties from the holder (the first) to the announcing one (the last). Reversed, it is the
    /// way back to the holder.
    /// </summary>
    public ImmutableArray<string> Path { get; }

    /// <summary>
    /// Gets the full name of the party that states the item (the first of the <see cref="Path"/>).
    /// </summary>
    public string Holder => Path[0];

    /// <summary>
    /// Gets the full name of the announcing party (the last of the <see cref="Path"/>).
    /// </summary>
    public string Announcer => Path[^1];

    /// <summary>
    /// Gets the number of parties of the <see cref="Path"/>.
    /// </summary>
    public int Length => Path.Length;

    /// <summary>
    /// Returns this item as announced by the party one hop further: its full name is the new last one of the <see cref="Path"/>.
    /// Call <see cref="TryAccept"/> first.
    /// </summary>
    /// <param name="fullName">The full name of the party.</param>
    /// <returns>The extended item.</returns>
    public CollectedItem Extend( string fullName ) => new CollectedItem( Value, Path.Add( fullName ) );

    /// <summary>
    /// Checks an item received from a remote, before a local party collects it: its last party is the announcing remote
    /// (a remote only vouches for its own step), the local party is not in it (no loop) and it can be extended (it is
    /// shorter than <see cref="PartyRoute.MaxLength"/>).
    /// </summary>
    /// <param name="announcerFullName">The full name of the remote that announced the item.</param>
    /// <param name="localFullName">The full name of the local party.</param>
    /// <param name="reason">Why the item is refused.</param>
    /// <returns>True when the item can be collected, false otherwise.</returns>
    public bool TryAccept( string announcerFullName, string localFullName, [NotNullWhen( false )] out string? reason )
    {
        if( Announcer != announcerFullName )
        {
            reason = $"its last party is '{Announcer}', not the announcing remote '{announcerFullName}'";
        }
        else if( Path.Contains( localFullName ) )
        {
            reason = $"it went through '{localFullName}' (a loop)";
        }
        else if( Path.Length >= PartyRoute.MaxLength )
        {
            reason = $"its path has already {PartyRoute.MaxLength} parties";
        }
        else
        {
            reason = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets whether this item and another one are the same item: the same <see cref="Holder"/> and the same <see cref="Value"/>
    /// (whatever their paths).
    /// </summary>
    /// <param name="other">The other item.</param>
    /// <returns>True when they are the same item.</returns>
    public bool IsSameItem( CollectedItem other ) => Holder == other.Holder && Value == other.Value;

    /// <summary>
    /// Orders the items: the <see cref="Holder"/>, then the <see cref="Value"/> (ordinal), so that a description is the same
    /// whatever the order in which the items are found.
    /// </summary>
    public static readonly IComparer<CollectedItem> Comparer = Comparer<CollectedItem>.Create( ( a, b ) =>
    {
        int c = StringComparer.Ordinal.Compare( a.Holder, b.Holder );
        return c != 0 ? c : StringComparer.Ordinal.Compare( a.Value, b.Value );
    } );

    /// <inheritdoc />
    public bool Equals( CollectedItem? other ) => other != null && Value == other.Value && Path.SequenceEqual( other.Path );

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as CollectedItem );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( Value, Path.Length, Holder );

    /// <summary>
    /// Returns "Holder > ... > Announcer: Value".
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{string.Join( " > ", Path )}: {Value}";
}
