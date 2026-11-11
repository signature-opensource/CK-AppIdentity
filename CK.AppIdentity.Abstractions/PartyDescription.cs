using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;

namespace CK.AppIdentity;

/// <summary>
/// What a party states about itself: its public URL, its client applications, its roles and its encryption key. A local party's description
/// comes from its "Local:PublicUrl" configuration, the contributions of the feature drivers and its
/// asymmetric features; a remote's description is what the remote announced (see <see cref="IRemoteParty.SetDescription"/>).
/// <para>
/// The URL of a client application is derived (<see cref="GetClientApplicationUrl"/>): it is never stored.
/// </para>
/// </summary>
public sealed class PartyDescription : IEquatable<PartyDescription>
{
    /// <summary>
    /// The empty description: no public URL, no client application, no role.
    /// </summary>
    public static readonly PartyDescription Empty = new PartyDescription( null, ImmutableArray<ClientApplicationDescription>.Empty );

    /// <summary>
    /// Initializes a new <see cref="PartyDescription"/>.
    /// </summary>
    /// <param name="publicUrl">The public URL (see <see cref="TryParsePublicUrl"/>).</param>
    /// <param name="clientApplications">The client applications.</param>
    /// <param name="roles">The roles (see <see cref="Roles"/>): they are sorted and deduplicated.</param>
    /// <param name="data">The data announcements (see <see cref="Data"/>).</param>
    /// <param name="routes">The routes (see <see cref="Routes"/>): per route name, at most one route per host.</param>
    /// <param name="encryptionKey">The encryption key (see <see cref="EncryptionKey"/>).</param>
    /// <param name="collected">
    /// The items collected along the routes (see <see cref="Collected"/>): at most <see cref="CollectedItem.MaxCount"/>
    /// distinct items per route name. They are sorted (see <see cref="CollectedItem.Comparer"/>).
    /// </param>
    public PartyDescription( Uri? publicUrl,
                             ImmutableArray<ClientApplicationDescription> clientApplications,
                             IEnumerable<string>? roles = null,
                             IEnumerable<KeyValuePair<string, string>>? data = null,
                             IEnumerable<KeyValuePair<string, IEnumerable<PartyRoute>>>? routes = null,
                             PartyEncryptionKey? encryptionKey = null,
                             IEnumerable<KeyValuePair<string, IEnumerable<CollectedItem>>>? collected = null )
    {
        EncryptionKey = encryptionKey;
        Throw.CheckArgument( publicUrl == null || IsValidPublicUrl( publicUrl, out _ ) );
        PublicUrl = publicUrl;
        ClientApplications = clientApplications.IsDefault ? ImmutableArray<ClientApplicationDescription>.Empty : clientApplications;
        Roles = roles == null
                    ? ImmutableArray<string>.Empty
                    : roles.Where( r => !string.IsNullOrWhiteSpace( r ) ).Distinct( StringComparer.Ordinal ).Order( StringComparer.Ordinal ).ToImmutableArray();
        Data = ToNamed( data, "data announcement" );
        Routes = ToRoutes( routes );
        Collected = ToCollected( collected );
    }

    static ImmutableSortedDictionary<string, ImmutableArray<CollectedItem>> ToCollected( IEnumerable<KeyValuePair<string, IEnumerable<CollectedItem>>>? collected )
    {
        var b = ImmutableSortedDictionary.CreateBuilder<string, ImmutableArray<CollectedItem>>( StringComparer.Ordinal );
        if( collected != null )
        {
            foreach( var (name, items) in collected )
            {
                if( !TryNormalizeCollected( name, items, out var normalized, out var error ) ) Throw.ArgumentException( error );
                if( normalized.Length > 0 ) b.Add( name, normalized );
            }
        }
        return b.ToImmutable();
    }

    /// <summary>
    /// Checks and sorts the items collected for a route name (see <see cref="Collected"/>): a valid name, at most
    /// <see cref="CollectedItem.MaxCount"/> items, no two items with the same holder and value.
    /// </summary>
    /// <param name="name">The route name.</param>
    /// <param name="items">The items.</param>
    /// <param name="normalized">The sorted items on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryNormalizeCollected( string name,
                                              IEnumerable<CollectedItem>? items,
                                              out ImmutableArray<CollectedItem> normalized,
                                              [NotNullWhen( false )] out string? error )
    {
        normalized = ImmutableArray<CollectedItem>.Empty;
        if( !IsValidName( name ) )
        {
            error = $"Invalid route name '{name}' of collected items: a non empty name without whitespace is expected.";
            return false;
        }
        var sorted = (items ?? []).ToArray();
        if( sorted.Any( i => i == null ) )
        {
            error = $"The collected items of '{name}' contain a null item.";
            return false;
        }
        if( sorted.Length > CollectedItem.MaxCount )
        {
            error = $"The route '{name}' has {sorted.Length} collected items: at most {CollectedItem.MaxCount} are allowed.";
            return false;
        }
        Array.Sort( sorted, CollectedItem.Comparer );
        for( int i = 1; i < sorted.Length; ++i )
        {
            if( sorted[i - 1].IsSameItem( sorted[i] ) )
            {
                error = $"The route '{name}' has the same collected item twice: '{sorted[i]}'.";
                return false;
            }
        }
        normalized = ImmutableArray.Create( sorted );
        error = null;
        return true;
    }

    static ImmutableSortedDictionary<string, ImmutableArray<PartyRoute>> ToRoutes( IEnumerable<KeyValuePair<string, IEnumerable<PartyRoute>>>? routes )
    {
        var b = ImmutableSortedDictionary.CreateBuilder<string, ImmutableArray<PartyRoute>>( StringComparer.Ordinal );
        if( routes != null )
        {
            foreach( var (name, items) in routes )
            {
                if( !TryNormalizeRoutes( name, items, out var normalized, out var error ) ) Throw.ArgumentException( error );
                if( normalized.Length > 0 ) b.Add( name, normalized );
            }
        }
        return b.ToImmutable();
    }

    /// <summary>
    /// Checks and sorts the routes of a route name (see <see cref="Routes"/>): a valid name, at most one route per host,
    /// sorted by host.
    /// </summary>
    /// <param name="name">The route name.</param>
    /// <param name="routes">The routes.</param>
    /// <param name="normalized">The sorted routes on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryNormalizeRoutes( string name,
                                           IEnumerable<PartyRoute>? routes,
                                           out ImmutableArray<PartyRoute> normalized,
                                           [NotNullWhen( false )] out string? error )
    {
        normalized = ImmutableArray<PartyRoute>.Empty;
        if( !IsValidName( name ) )
        {
            error = $"Invalid route name '{name}': a non empty name without whitespace is expected.";
            return false;
        }
        var sorted = (routes ?? []).ToArray();
        if( sorted.Any( r => r == null ) )
        {
            error = $"The routes of '{name}' contain a null route.";
            return false;
        }
        Array.Sort( sorted, ( a, b ) => StringComparer.Ordinal.Compare( a.Host, b.Host ) );
        for( int i = 1; i < sorted.Length; ++i )
        {
            if( sorted[i - 1].Host == sorted[i].Host )
            {
                error = $"The route '{name}' has two routes to '{sorted[i].Host}'.";
                return false;
            }
        }
        normalized = ImmutableArray.Create( sorted );
        error = null;
        return true;
    }

    static ImmutableSortedDictionary<string, T> ToNamed<T>( IEnumerable<KeyValuePair<string, T>>? items, string what ) where T : class
    {
        if( items == null ) return ImmutableSortedDictionary.Create<string, T>( StringComparer.Ordinal );
        var b = ImmutableSortedDictionary.CreateBuilder<string, T>( StringComparer.Ordinal );
        foreach( var (name, value) in items )
        {
            Throw.CheckArgument( $"Invalid {what} name '{name}': a non empty name without whitespace is expected.", IsValidName( name ) );
            Throw.CheckArgument( $"The {what} '{name}' has no value.", value != null );
            b.Add( name, value );
        }
        return b.ToImmutable();
    }

    /// <summary>
    /// Gets whether a name is a valid name of a <see cref="Data"/> announcement or of a <see cref="Routes"/>: a non empty
    /// name without whitespace.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns>True when the name is valid.</returns>
    public static bool IsValidName( [NotNullWhen( true )] string? name ) => !string.IsNullOrEmpty( name ) && !name.Any( char.IsWhiteSpace );

    /// <summary>
    /// Gets the data announcements of the party, by name: what a feature announces about the party without routing it
    /// (a string by name). Announced, compared and persisted with the rest of the description.
    /// </summary>
    public ImmutableSortedDictionary<string, string> Data { get; }

    /// <summary>
    /// Gets the routes known by the party, by name (see <see cref="PartyRoute"/>): per name, the route to each party that
    /// hosts it, sorted by host ("Enlistment": the approval page of the System; a name can have several hosts). A route
    /// name has at least one route. Announced and compared, never persisted: a route counts only from a connected remote.
    /// </summary>
    public ImmutableSortedDictionary<string, ImmutableArray<PartyRoute>> Routes { get; }

    /// <summary>
    /// Finds the route of a name that leads to a host.
    /// </summary>
    /// <param name="name">The route name.</param>
    /// <param name="host">The full name of the host.</param>
    /// <returns>The route, or null.</returns>
    public PartyRoute? FindRoute( string name, string host )
        => Routes.TryGetValue( name, out var routes ) ? routes.FirstOrDefault( r => r.Host == host ) : null;

    /// <summary>
    /// Gets the items collected along the routes, by route name (see <see cref="CollectedItem"/>): the items stated by the
    /// party and the ones its connected remotes announced for a route whose next hop is this party, on their way toward
    /// the host of the route ("Enlistment": the pending approvals of the System). A route name has at least one item.
    /// Announced and compared, never persisted: an item counts only from a connected remote.
    /// </summary>
    public ImmutableSortedDictionary<string, ImmutableArray<CollectedItem>> Collected { get; }

    /// <summary>
    /// Gets the current encryption key of the party, signed by it (see <see cref="PartyEncryptionKey"/>): what another
    /// party encrypts for it with. Null when the party publishes none. Announced and compared, never persisted: a key is
    /// short-lived, it counts only from a connected remote.
    /// </summary>
    public PartyEncryptionKey? EncryptionKey { get; }

    /// <summary>
    /// Gets the roles of the party: the asymmetric features that the party IS (its "Local" allows them and their
    /// Local side is installed), sorted. A party that relays an asymmetric feature to a remote checks that the remote
    /// announces it.
    /// </summary>
    public ImmutableArray<string> Roles { get; }

    /// <summary>
    /// Returns this description with other <see cref="Roles"/>.
    /// </summary>
    /// <param name="roles">The roles.</param>
    /// <returns>The description.</returns>
    public PartyDescription WithRoles( IEnumerable<string>? roles ) => new PartyDescription( PublicUrl, ClientApplications, roles, Data, ToEnumerable( Routes ), EncryptionKey, ToEnumerable( Collected ) );

    static IEnumerable<KeyValuePair<string, IEnumerable<T>>> ToEnumerable<T>( IReadOnlyDictionary<string, ImmutableArray<T>> items )
        => items.Select( kv => new KeyValuePair<string, IEnumerable<T>>( kv.Key, kv.Value ) );

    /// <summary>
    /// Gets the public URL of the party: an absolute "http" or "https" URL without path, query nor fragment.
    /// Null when the party has no web endpoint.
    /// </summary>
    public Uri? PublicUrl { get; }

    /// <summary>
    /// Gets the client applications served by the party.
    /// </summary>
    public ImmutableArray<ClientApplicationDescription> ClientApplications { get; }

    /// <summary>
    /// Gets the URL of a client application (without trailing slash: "https://acme.com", "https://acme.com/mini-app"),
    /// or null when there is no <see cref="PublicUrl"/> or no such client application.
    /// </summary>
    /// <param name="name">The client application name. Null for the default one (the one served at "/").</param>
    /// <returns>The URL or null.</returns>
    public string? GetClientApplicationUrl( string? name = null )
    {
        if( PublicUrl == null ) return null;
        var app = name == null
                    ? ClientApplications.FirstOrDefault( a => a.IsDefault )
                    : ClientApplications.FirstOrDefault( a => a.Name == name );
        if( app == null ) return null;
        return PublicUrl.GetLeftPart( UriPartial.Authority ) + app.RequestPath.TrimEnd( '/' );
    }

    /// <summary>
    /// Parses a public URL: an absolute "http" or "https" URL without user information, path, query nor fragment.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="url">The URL on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParsePublicUrl( string? value, [NotNullWhen( true )] out Uri? url, [NotNullWhen( false )] out string? error )
    {
        url = null;
        if( string.IsNullOrWhiteSpace( value ) || !Uri.TryCreate( value.Trim(), UriKind.Absolute, out var u ) )
        {
            error = $"Invalid public URL '{value}': an absolute URL is expected.";
            return false;
        }
        if( !IsValidPublicUrl( u, out error ) ) return false;
        url = u;
        return true;
    }

    static bool IsValidPublicUrl( Uri u, [NotNullWhen( false )] out string? error )
    {
        if( !u.IsAbsoluteUri || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) )
        {
            error = $"Invalid public URL '{u}': the scheme must be http or https.";
            return false;
        }
        if( u.UserInfo.Length > 0 || u.AbsolutePath != "/" || u.Query.Length > 0 || u.Fragment.Length > 0 )
        {
            error = $"Invalid public URL '{u}': no user information, path, query nor fragment is allowed.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Writes this description as a JSON object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        w.WriteStartObject();
        if( PublicUrl != null ) w.WriteString( "PublicUrl", PublicUrl.ToString() );
        w.WriteStartArray( "ClientApplications" );
        foreach( var a in ClientApplications )
        {
            w.WriteStartObject();
            w.WriteString( "Name", a.Name );
            w.WriteString( "RequestPath", a.RequestPath );
            if( !a.Facts.IsEmpty )
            {
                // A key has a set of values: { "Admin": [""], "Enlistment": ["app-identity/enlist"] }.
                w.WriteStartObject( "Facts" );
                foreach( var (key, values) in a.Facts.Values )
                {
                    w.WriteStartArray( key );
                    foreach( var v in values ) w.WriteStringValue( v );
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
        if( Roles.Length > 0 )
        {
            w.WriteStartArray( "Roles" );
            foreach( var r in Roles ) w.WriteStringValue( r );
            w.WriteEndArray();
        }
        // The routes, the collected items and the encryption key are not persisted: they count only from a connected remote.
        if( Data.Count > 0 )
        {
            w.WriteStartObject( "Data" );
            foreach( var (name, value) in Data ) w.WriteString( name, value );
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads a description written by <see cref="Write"/>.
    /// </summary>
    /// <param name="e">The JSON object.</param>
    /// <param name="description">The description on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryRead( JsonElement e, [NotNullWhen( true )] out PartyDescription? description, [NotNullWhen( false )] out string? error )
    {
        description = null;
        if( e.ValueKind != JsonValueKind.Object )
        {
            error = "A JSON object is expected.";
            return false;
        }
        Uri? url = null;
        if( e.TryGetProperty( "PublicUrl", out var u ) && !TryParsePublicUrl( u.GetString(), out url, out error ) )
        {
            return false;
        }
        var apps = ImmutableArray.CreateBuilder<ClientApplicationDescription>();
        if( e.TryGetProperty( "ClientApplications", out var a ) && a.ValueKind == JsonValueKind.Array )
        {
            foreach( var app in a.EnumerateArray() )
            {
                var name = app.TryGetProperty( "Name", out var n ) ? n.GetString() : null;
                var path = app.TryGetProperty( "RequestPath", out var p ) ? p.GetString() : null;
                if( string.IsNullOrWhiteSpace( name ) || string.IsNullOrWhiteSpace( path ) || path[0] != '/' )
                {
                    error = "Invalid client application: a Name and a RequestPath that starts with '/' are expected.";
                    return false;
                }
                ClientApplicationFacts? facts = null;
                if( app.TryGetProperty( "Facts", out var f ) && f.ValueKind == JsonValueKind.Object )
                {
                    var pairs = new List<KeyValuePair<string, string>>();
                    foreach( var fact in f.EnumerateObject() )
                    {
                        if( fact.Value.ValueKind != JsonValueKind.Array
                            || fact.Value.EnumerateArray().Any( v => v.ValueKind != JsonValueKind.String ) )
                        {
                            error = $"Invalid fact '{fact.Name}' of the client application '{name}': an array of strings is expected.";
                            return false;
                        }
                        foreach( var v in fact.Value.EnumerateArray() ) pairs.Add( new KeyValuePair<string, string>( fact.Name, v.GetString()! ) );
                    }
                    if( !ClientApplicationFacts.TryCreate( pairs, out facts, out error ) ) return false;
                }
                apps.Add( new ClientApplicationDescription( name, path, facts ) );
            }
        }
        List<string>? roles = null;
        if( e.TryGetProperty( "Roles", out var rs ) && rs.ValueKind == JsonValueKind.Array )
        {
            roles = new List<string>();
            foreach( var r in rs.EnumerateArray() )
            {
                var role = r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                if( string.IsNullOrWhiteSpace( role ) )
                {
                    error = "Invalid role: a non empty string is expected.";
                    return false;
                }
                roles.Add( role );
            }
        }
        List<KeyValuePair<string, string>>? data = null;
        if( e.TryGetProperty( "Data", out var ds ) && ds.ValueKind == JsonValueKind.Object )
        {
            data = new List<KeyValuePair<string, string>>();
            foreach( var d in ds.EnumerateObject() )
            {
                if( !IsValidName( d.Name ) || d.Value.ValueKind != JsonValueKind.String )
                {
                    error = $"Invalid data announcement '{d.Name}': a name without whitespace and a string value are expected.";
                    return false;
                }
                data.Add( new KeyValuePair<string, string>( d.Name, d.Value.GetString()! ) );
            }
        }
        description = new PartyDescription( url, apps.ToImmutable(), roles, data );
        error = null;
        return true;
    }

    /// <inheritdoc />
    public bool Equals( PartyDescription? other ) => other != null
                                                     && Equals( PublicUrl, other.PublicUrl )
                                                     && ClientApplications.SequenceEqual( other.ClientApplications )
                                                     && Roles.SequenceEqual( other.Roles )
                                                     && Data.SequenceEqual( other.Data )
                                                     && ArraysEquals( Routes, other.Routes )
                                                     && ArraysEquals( Collected, other.Collected )
                                                     && Equals( EncryptionKey, other.EncryptionKey );

    // An ImmutableArray compares its array reference: the items are compared one by one.
    static bool ArraysEquals<T>( ImmutableSortedDictionary<string, ImmutableArray<T>> a,
                                 ImmutableSortedDictionary<string, ImmutableArray<T>> b )
    {
        if( a.Count != b.Count ) return false;
        foreach( var (name, items) in a )
        {
            if( !b.TryGetValue( name, out var other ) || !items.SequenceEqual( other ) ) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as PartyDescription );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( PublicUrl, ClientApplications.Length );

    /// <inheritdoc />
    public override string ToString()
    {
        var s = $"{PublicUrl?.ToString() ?? "(no public URL)"} [{string.Join( ", ", ClientApplications )}]";
        if( Roles.Length > 0 ) s += $" (Roles: {string.Join( ", ", Roles )})";
        if( Data.Count > 0 ) s += $" (Data: {string.Join( ", ", Data.Keys )})";
        if( Routes.Count > 0 ) s += $" (Routes: {string.Join( ", ", Routes.SelectMany( r => r.Value.Select( v => $"{r.Key}: {v}" ) ) )})";
        if( Collected.Count > 0 ) s += $" (Collected: {string.Join( ", ", Collected.Select( c => $"{c.Key}: {c.Value.Length}" ) )})";
        if( EncryptionKey != null ) s += $" (EncryptionKey: {EncryptionKey})";
        return s;
    }

    /// <summary>
    /// The first phase of the description of a local party: its configuration and the facts that the feature drivers
    /// contribute (the public URL, the client applications, the data announcements, the hosted routes). The second
    /// phase (<see cref="DerivedBuilder"/>) derives from them.
    /// </summary>
    public sealed class Builder
    {
        readonly List<ClientApplicationDescription> _apps = new();
        readonly Dictionary<string, string> _data = new( StringComparer.Ordinal );
        readonly Dictionary<string, string?> _hostedRoutes = new( StringComparer.Ordinal );
        readonly Dictionary<string, IReadOnlyList<string>> _collected = new( StringComparer.Ordinal );

        /// <summary>
        /// Gets the client applications added so far.
        /// </summary>
        public IReadOnlyList<ClientApplicationDescription> ClientApplications => _apps;

        /// <summary>
        /// Gets the values of the items stated by the party so far, by route name (see <see cref="Collect"/>).
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Collected => _collected;

        /// <summary>
        /// States an item of the party to collect along the route <paramref name="name"/> (see <see cref="PartyDescription.Collected"/>):
        /// the second phase gives it to the party when it has a route of this name, and the hops carry it toward the host.
        /// Stating the same value twice is ignored.
        /// </summary>
        /// <param name="name">The name of the route: a non empty name without whitespace.</param>
        /// <param name="value">The value: a non empty string of at most <see cref="CollectedItem.MaxValueLength"/> characters.</param>
        public void Collect( string name, string value )
        {
            Throw.CheckArgument( IsValidName( name ) );
            Throw.CheckArgument( !string.IsNullOrEmpty( value ) && value.Length <= CollectedItem.MaxValueLength );
            if( !_collected.TryGetValue( name, out var values ) ) _collected.Add( name, values = new List<string>() );
            var list = (List<string>)values;
            if( !list.Contains( value ) ) list.Add( value );
        }

        /// <summary>
        /// Gets the data announcements set so far.
        /// </summary>
        public IReadOnlyDictionary<string, string> Data => _data;

        /// <summary>
        /// Gets the routes hosted by the party, so far: the target of each route name (null when it has none).
        /// </summary>
        public IReadOnlyDictionary<string, string?> HostedRoutes => _hostedRoutes;

        /// <summary>
        /// Sets a data announcement (see <see cref="PartyDescription.Data"/>).
        /// </summary>
        /// <param name="name">The name of the announcement: a non empty name without whitespace.</param>
        /// <param name="value">The value.</param>
        public void SetData( string name, string value )
        {
            Throw.CheckArgument( IsValidName( name ) );
            Throw.CheckNotNullArgument( value );
            _data[name] = value;
        }

        /// <summary>
        /// States that the party hosts the route <paramref name="name"/>, with an optional target (see <see cref="PartyRoute"/>):
        /// the second phase gives the party this route, and its remotes relay it.
        /// </summary>
        /// <param name="name">The name of the route: a non empty name without whitespace.</param>
        /// <param name="target">The target, when the route has one: a string (an URL...).</param>
        public void HostRoute( string name, string? target = null )
        {
            Throw.CheckArgument( IsValidName( name ) );
            _hostedRoutes[name] = target;
        }

        /// <summary>
        /// Gets or sets the public URL. The "Local:PublicUrl" configuration sets it first: a contributor sets it only
        /// when it is null.
        /// </summary>
        public Uri? PublicUrl { get; set; }

        /// <summary>
        /// Gets or sets the encryption key of the party (see <see cref="PartyDescription.EncryptionKey"/>): set by the key
        /// management.
        /// </summary>
        public PartyEncryptionKey? EncryptionKey { get; set; }

        /// <summary>
        /// Adds a client application.
        /// </summary>
        /// <param name="name">The client application name.</param>
        /// <param name="requestPath">Its request path ("/" for the default one).</param>
        public void AddClientApplication( string name, string requestPath )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            Throw.CheckArgument( requestPath != null && requestPath.StartsWith( '/' ) );
            _apps.Add( new ClientApplicationDescription( name, requestPath ) );
        }

        /// <summary>
        /// Adds a client application, with its facts.
        /// </summary>
        /// <param name="application">The client application.</param>
        public void AddClientApplication( ClientApplicationDescription application )
        {
            Throw.CheckNotNullArgument( application );
            _apps.Add( application );
        }

    }

    /// <summary>
    /// The second phase of the description of a local party: it reads what the first phase states (see
    /// <see cref="Builder"/>) and sets what derives from it (the routes).
    /// </summary>
    public sealed class DerivedBuilder
    {
        readonly Builder _facts;
        readonly Dictionary<string, ImmutableArray<PartyRoute>> _routes = new( StringComparer.Ordinal );
        readonly Dictionary<string, ImmutableArray<CollectedItem>> _collected = new( StringComparer.Ordinal );

        /// <summary>
        /// Initializes the second phase from the first one.
        /// </summary>
        /// <param name="facts">The first phase.</param>
        public DerivedBuilder( Builder facts )
        {
            Throw.CheckNotNullArgument( facts );
            _facts = facts;
        }

        /// <summary>
        /// Gets the public URL stated by the first phase.
        /// </summary>
        public Uri? PublicUrl => _facts.PublicUrl;

        /// <summary>
        /// Gets the client applications stated by the first phase.
        /// </summary>
        public IReadOnlyList<ClientApplicationDescription> ClientApplications => _facts.ClientApplications;

        /// <summary>
        /// Gets the data announcements stated by the first phase.
        /// </summary>
        public IReadOnlyDictionary<string, string> Data => _facts.Data;

        /// <summary>
        /// Gets the routes hosted by the party (the target of each route name, null when it has none), stated by the first
        /// phase.
        /// </summary>
        public IReadOnlyDictionary<string, string?> HostedRoutes => _facts.HostedRoutes;

        /// <summary>
        /// Gets the routes set so far.
        /// </summary>
        public IReadOnlyDictionary<string, ImmutableArray<PartyRoute>> Routes => _routes;

        /// <summary>
        /// Sets (or removes when <paramref name="routes"/> is null or empty) the routes of a name (see
        /// <see cref="PartyDescription.Routes"/>): at most one route per host.
        /// </summary>
        /// <param name="name">The name of the route: a non empty name without whitespace.</param>
        /// <param name="routes">The routes, null to remove them.</param>
        public void SetRoutes( string name, IEnumerable<PartyRoute>? routes )
        {
            if( !TryNormalizeRoutes( name, routes, out var normalized, out var error ) ) Throw.ArgumentException( error );
            if( normalized.Length == 0 ) _routes.Remove( name );
            else _routes[name] = normalized;
        }

        /// <summary>
        /// Gets the values of the items stated by the party, by route name, stated by the first phase.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> OwnCollected => _facts.Collected;

        /// <summary>
        /// Gets the collected items set so far.
        /// </summary>
        public IReadOnlyDictionary<string, ImmutableArray<CollectedItem>> Collected => _collected;

        /// <summary>
        /// Sets (or removes when <paramref name="items"/> is null or empty) the items collected along a route (see
        /// <see cref="PartyDescription.Collected"/>): at most <see cref="CollectedItem.MaxCount"/> distinct items.
        /// </summary>
        /// <param name="name">The name of the route: a non empty name without whitespace.</param>
        /// <param name="items">The items, null to remove them.</param>
        public void SetCollected( string name, IEnumerable<CollectedItem>? items )
        {
            if( !TryNormalizeCollected( name, items, out var normalized, out var error ) ) Throw.ArgumentException( error );
            if( normalized.Length == 0 ) _collected.Remove( name );
            else _collected[name] = normalized;
        }

        /// <summary>
        /// Builds the description.
        /// </summary>
        /// <returns>The description.</returns>
        public PartyDescription Build() => new PartyDescription( _facts.PublicUrl,
                                                                 _facts.ClientApplications.ToImmutableArray(),
                                                                 null,
                                                                 _facts.Data,
                                                                 ToEnumerable( _routes ),
                                                                 _facts.EncryptionKey,
                                                                 ToEnumerable( _collected ) );
    }
}

/// <summary>
/// A client application of a party (see <see cref="PartyDescription.ClientApplications"/>): its name, its request path
/// and its facts.
/// </summary>
public sealed class ClientApplicationDescription : IEquatable<ClientApplicationDescription>
{
    /// <summary>
    /// Initializes a new client application description.
    /// </summary>
    /// <param name="name">The client application name.</param>
    /// <param name="requestPath">Its request path: "/" for the default one, "/mini-app" for another one.</param>
    /// <param name="facts">Its facts. Null for none.</param>
    public ClientApplicationDescription( string name, string requestPath, ClientApplicationFacts? facts = null )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckNotNullArgument( requestPath );
        Name = name;
        RequestPath = requestPath;
        Facts = facts ?? ClientApplicationFacts.Empty;
    }

    /// <summary>
    /// Gets the client application name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets its request path: "/" for the default one, "/mini-app" for another one.
    /// </summary>
    public string RequestPath { get; }

    /// <summary>
    /// Gets its facts: what the client is, stated by what it is built from (the PartyClientFact attribute of
    /// CK.AppIdentity.PartyClient). Never null, <see cref="ClientApplicationFacts.IsEmpty"/> when it has none.
    /// </summary>
    public ClientApplicationFacts Facts { get; }

    /// <summary>
    /// Gets whether this is the default client application of the party (served at "/").
    /// </summary>
    public bool IsDefault => RequestPath == "/";

    /// <inheritdoc />
    public bool Equals( ClientApplicationDescription? other ) => other != null
                                                                 && Name == other.Name
                                                                 && RequestPath == other.RequestPath
                                                                 && Facts.Equals( other.Facts );

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as ClientApplicationDescription );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( Name, RequestPath, Facts );

    /// <summary>
    /// Returns "Name (RequestPath)", followed by the facts when there are some.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => Facts.IsEmpty ? $"{Name} ({RequestPath})" : $"{Name} ({RequestPath}, Facts: {Facts})";
}
