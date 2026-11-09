using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// A route to a party that hosts something (an approval page, a command handler...), announced in the
/// <see cref="PartyDescription.Routes"/> of the parties: each party knows the route of its nearest connected remote, one
/// hop more, so that any party of a System knows where the hosting party is and through which parties it is reached.
/// <para>
/// The <see cref="Target"/> is a mere string, never interpreted by the route: the hosting party sets it (an URL, the name
/// of a command...) and the other parties pass it along unchanged. The <see cref="Path"/> is the list of the parties
/// from the announcing one to the hosting one, both included: a party that finds its own name in a path ignores it, so
/// that no loop is possible.
/// </para>
/// </summary>
public sealed class PartyRoute : IEquatable<PartyRoute>
{
    /// <summary>
    /// The maximal number of parties in a <see cref="Path"/>.
    /// </summary>
    public const int MaxLength = 16;

    /// <summary>
    /// Initializes a new route.
    /// </summary>
    /// <param name="target">The target: a string set by the hosting party.</param>
    /// <param name="path">The full names of the parties, from the announcing one to the hosting one (1 to <see cref="MaxLength"/> distinct names).</param>
    public PartyRoute( string target, IEnumerable<string> path )
    {
        Throw.CheckNotNullArgument( target );
        Throw.CheckNotNullArgument( path );
        var p = path.ToImmutableArray();
        Throw.CheckArgument( "A path has 1 to MaxLength parties.", p.Length >= 1 && p.Length <= MaxLength );
        Throw.CheckArgument( "A path contains distinct, non empty full names.",
                             p.All( n => !string.IsNullOrWhiteSpace( n ) ) && p.Distinct( StringComparer.Ordinal ).Count() == p.Length );
        Target = target;
        Path = p;
    }

    /// <summary>
    /// Creates the route of the hosting party itself: a path of one name.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="hostFullName">The full name of the hosting party.</param>
    /// <returns>The route.</returns>
    public static PartyRoute Hosted( string target, string hostFullName ) => new PartyRoute( target, [hostFullName] );

    /// <summary>
    /// Gets the target: a string set by the hosting party (an URL, the name of a command...).
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Gets the full names of the parties from the announcing one (the first) to the hosting one (the last).
    /// </summary>
    public ImmutableArray<string> Path { get; }

    /// <summary>
    /// Gets the full name of the announcing party (the first of the <see cref="Path"/>).
    /// </summary>
    public string Announcer => Path[0];

    /// <summary>
    /// Gets the full name of the hosting party (the last of the <see cref="Path"/>).
    /// </summary>
    public string Host => Path[^1];

    /// <summary>
    /// Gets the number of parties of the <see cref="Path"/>.
    /// </summary>
    public int Length => Path.Length;

    /// <summary>
    /// Gets the full name of the party after the announcing one: null when the announcing party is the hosting one.
    /// </summary>
    public string? NextHop => Path.Length > 1 ? Path[1] : null;

    /// <summary>
    /// Returns this route as seen from a party one hop before: its full name is the new first one of the <see cref="Path"/>.
    /// Call <see cref="TryAccept"/> first: the party must not be in the path, and the path must be shorter than <see cref="MaxLength"/>.
    /// </summary>
    /// <param name="fullName">The full name of the party.</param>
    /// <returns>The extended route.</returns>
    public PartyRoute Extend( string fullName ) => new PartyRoute( Target, Path.Insert( 0, fullName ) );

    /// <summary>
    /// Checks a route received from a remote, before a local party uses it: its first party is the announcing remote (a
    /// remote only vouches for its own next step), the local party is not in it (no loop) and it can be extended (it is
    /// shorter than <see cref="MaxLength"/>).
    /// </summary>
    /// <param name="announcerFullName">The full name of the remote that announced the route.</param>
    /// <param name="localFullName">The full name of the local party.</param>
    /// <param name="reason">Why the route is refused.</param>
    /// <returns>True when the route can be used, false otherwise.</returns>
    public bool TryAccept( string announcerFullName, string localFullName, [NotNullWhen( false )] out string? reason )
    {
        if( Announcer != announcerFullName )
        {
            reason = $"its first party is '{Announcer}', not the announcing remote '{announcerFullName}'";
        }
        else if( Path.Contains( localFullName ) )
        {
            reason = $"it goes through '{localFullName}' (a loop)";
        }
        else if( Path.Length >= MaxLength )
        {
            reason = $"its path has already {MaxLength} parties";
        }
        else
        {
            reason = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Selects the best route among accepted ones: the shortest one, then the ordinal full name of its announcing party
    /// (the next hop of the local party), so that every party chooses the same way.
    /// </summary>
    /// <param name="routes">The accepted routes.</param>
    /// <returns>The best route, or null when there is none.</returns>
    public static PartyRoute? SelectBest( IEnumerable<PartyRoute> routes )
    {
        Throw.CheckNotNullArgument( routes );
        PartyRoute? best = null;
        foreach( var r in routes )
        {
            if( best == null
                || r.Length < best.Length
                || (r.Length == best.Length && StringComparer.Ordinal.Compare( r.Announcer, best.Announcer ) < 0) )
            {
                best = r;
            }
        }
        return best;
    }

    /// <inheritdoc />
    public bool Equals( PartyRoute? other ) => other != null && Target == other.Target && Path.SequenceEqual( other.Path );

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as PartyRoute );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( Target, Path.Length, Host );

    /// <summary>
    /// Returns "First > ... > Host (Target)".
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{string.Join( " > ", Path )} ({Target})";
}
