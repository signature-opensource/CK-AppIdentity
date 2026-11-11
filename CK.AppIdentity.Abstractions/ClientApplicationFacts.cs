using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The facts of a client application (see <see cref="ClientApplicationDescription.Facts"/>): a multimap of strings, a
/// key has a set of values. A fact without value is its key with the empty value ("Admin"); a fact with a value states
/// it ("Enlistment" = "app-identity/enlist"). Combining facts is a union: associative, commutative and idempotent. Each
/// consumer decides what it accepts (the transport, for instance, expects exactly one value for "Enlistment").
/// </summary>
public sealed class ClientApplicationFacts : IEquatable<ClientApplicationFacts>
{
    /// <summary>
    /// The maximal length of a value.
    /// </summary>
    public const int MaxValueLength = 256;

    /// <summary>
    /// No fact.
    /// </summary>
    public static readonly ClientApplicationFacts Empty = new ClientApplicationFacts( ImmutableSortedDictionary.Create<string, ImmutableArray<string>>( StringComparer.Ordinal ) );

    ClientApplicationFacts( ImmutableSortedDictionary<string, ImmutableArray<string>> values )
    {
        Values = values;
    }

    /// <summary>
    /// Creates facts from (key, value) pairs: the same pair twice is a single fact.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The facts.</returns>
    public static ClientApplicationFacts Create( IEnumerable<KeyValuePair<string, string>> facts )
    {
        Throw.CheckNotNullArgument( facts );
        if( !TryCreate( facts, out var result, out var error ) ) Throw.ArgumentException( nameof( facts ), error );
        return result;
    }

    /// <summary>
    /// Creates facts from (key, value) pairs: the same pair twice is a single fact.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The facts.</returns>
    public static ClientApplicationFacts Create( params (string Key, string Value)[] facts )
        => Create( facts.Select( f => new KeyValuePair<string, string>( f.Key, f.Value ) ) );

    /// <summary>
    /// Tries to create facts from (key, value) pairs: the same pair twice is a single fact.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <param name="result">The facts on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryCreate( IEnumerable<KeyValuePair<string, string>> facts,
                                  [NotNullWhen( true )] out ClientApplicationFacts? result,
                                  [NotNullWhen( false )] out string? error )
    {
        result = null;
        var sets = new SortedDictionary<string, SortedSet<string>>( StringComparer.Ordinal );
        foreach( var (key, value) in facts )
        {
            if( !IsValidKey( key ) )
            {
                error = $"Invalid fact key '{key}': a non empty name without whitespace, '=' nor ',' is expected.";
                return false;
            }
            if( !IsValidValue( value ) )
            {
                error = $"Invalid value of the fact '{key}': a string of at most {MaxValueLength} characters without control character is expected.";
                return false;
            }
            if( !sets.TryGetValue( key, out var set ) ) sets.Add( key, set = new SortedSet<string>( StringComparer.Ordinal ) );
            set.Add( value );
        }
        error = null;
        result = sets.Count == 0
                    ? Empty
                    : new ClientApplicationFacts( sets.ToImmutableSortedDictionary( kv => kv.Key, kv => kv.Value.ToImmutableArray(), StringComparer.Ordinal ) );
        return true;
    }

    /// <summary>
    /// Gets whether a string is a valid key: a non empty name without whitespace, '=' nor ','.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValidKey( [NotNullWhen( true )] string? key ) => !string.IsNullOrEmpty( key )
                                                                          && !key.Any( c => char.IsWhiteSpace( c ) || char.IsControl( c ) || c == '=' || c == ',' );

    /// <summary>
    /// Gets whether a string is a valid value: at most <see cref="MaxValueLength"/> characters without control character
    /// (the empty string is the value of a fact without value).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValidValue( [NotNullWhen( true )] string? value ) => value != null
                                                                              && value.Length <= MaxValueLength
                                                                              && !value.Any( char.IsControl );

    /// <summary>
    /// Gets the values of each key, sorted (the keys and their values, in ordinal order).
    /// </summary>
    public ImmutableSortedDictionary<string, ImmutableArray<string>> Values { get; }

    /// <summary>
    /// Gets whether there is no fact.
    /// </summary>
    public bool IsEmpty => Values.Count == 0;

    /// <summary>
    /// Gets whether a key is a fact (with any value).
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the key is a fact.</returns>
    public bool Contains( string key ) => Values.ContainsKey( key );

    /// <summary>
    /// Gets the values of a key: empty when the key is not a fact.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The values.</returns>
    public ImmutableArray<string> Get( string key ) => Values.TryGetValue( key, out var v ) ? v : ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets the facts as (key, value) pairs, in order.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> Pairs => Values.SelectMany( kv => kv.Value.Select( v => new KeyValuePair<string, string>( kv.Key, v ) ) );

    /// <summary>
    /// Returns the union of these facts and other ones.
    /// </summary>
    /// <param name="other">The other facts.</param>
    /// <returns>The union.</returns>
    public ClientApplicationFacts Union( ClientApplicationFacts other )
    {
        Throw.CheckNotNullArgument( other );
        if( other.IsEmpty ) return this;
        if( IsEmpty ) return other;
        return Create( Pairs.Concat( other.Pairs ) );
    }

    /// <inheritdoc />
    public bool Equals( ClientApplicationFacts? other )
    {
        if( other == null || other.Values.Count != Values.Count ) return false;
        foreach( var (key, values) in Values )
        {
            if( !other.Values.TryGetValue( key, out var o ) || !values.SequenceEqual( o ) ) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as ClientApplicationFacts );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( Values.Count, Values.Keys.FirstOrDefault() );

    /// <summary>
    /// Returns the facts, "Admin, Enlistment=app-identity/enlist": a fact without value is its key alone.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => string.Join( ", ", Pairs.Select( p => p.Value.Length == 0 ? p.Key : $"{p.Key}={p.Value}" ) );
}
