using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;

namespace CK.AppIdentity;

/// <summary>
/// What a party states about itself: its public URL, its client applications and its roles. A local party's description
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
    /// <param name="routes">The routes (see <see cref="Routes"/>).</param>
    public PartyDescription( Uri? publicUrl,
                             ImmutableArray<ClientApplicationDescription> clientApplications,
                             IEnumerable<string>? roles = null,
                             IEnumerable<KeyValuePair<string, string>>? data = null,
                             IEnumerable<KeyValuePair<string, PartyRoute>>? routes = null )
    {
        Throw.CheckArgument( publicUrl == null || IsValidPublicUrl( publicUrl, out _ ) );
        PublicUrl = publicUrl;
        ClientApplications = clientApplications.IsDefault ? ImmutableArray<ClientApplicationDescription>.Empty : clientApplications;
        Roles = roles == null
                    ? ImmutableArray<string>.Empty
                    : roles.Where( r => !string.IsNullOrWhiteSpace( r ) ).Distinct( StringComparer.Ordinal ).Order( StringComparer.Ordinal ).ToImmutableArray();
        Data = ToNamed( data, "data announcement" );
        Routes = ToNamed( routes, "route" );
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
    /// Gets the routes known by the party, by name (see <see cref="PartyRoute"/>): the route to the party that hosts
    /// something ("Enlistment": the approval page of the System). Announced and compared, never persisted: a route
    /// counts only from a connected remote.
    /// </summary>
    public ImmutableSortedDictionary<string, PartyRoute> Routes { get; }

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
    public PartyDescription WithRoles( IEnumerable<string>? roles ) => new PartyDescription( PublicUrl, ClientApplications, roles, Data, Routes );

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
            if( !a.Facts.IsEmpty ) w.WriteString( "Facts", a.Facts.ToString() );
            w.WriteEndObject();
        }
        w.WriteEndArray();
        if( Roles.Length > 0 )
        {
            w.WriteStartArray( "Roles" );
            foreach( var r in Roles ) w.WriteStringValue( r );
            w.WriteEndArray();
        }
        // The routes are not persisted: a route counts only from a connected remote.
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
                var facts = app.TryGetProperty( "Facts", out var f ) && f.ValueKind == JsonValueKind.String
                                ? ClientApplicationDescription.FactContext.FindOrCreate( f.GetString() )
                                : null;
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
                                                     && Routes.SequenceEqual( other.Routes );

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
        if( Routes.Count > 0 ) s += $" (Routes: {string.Join( ", ", Routes.Select( r => $"{r.Key}: {r.Value}" ) )})";
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
        readonly Dictionary<string, string> _hostedRoutes = new( StringComparer.Ordinal );

        /// <summary>
        /// Gets the client applications added so far.
        /// </summary>
        public IReadOnlyList<ClientApplicationDescription> ClientApplications => _apps;

        /// <summary>
        /// Gets the data announcements set so far.
        /// </summary>
        public IReadOnlyDictionary<string, string> Data => _data;

        /// <summary>
        /// Gets the routes hosted by the party, so far: the target of each route name.
        /// </summary>
        public IReadOnlyDictionary<string, string> HostedRoutes => _hostedRoutes;

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
        /// States that the party hosts the route <paramref name="name"/>, with a target (see <see cref="PartyRoute"/>):
        /// the second phase gives the party this route, and its remotes relay it.
        /// </summary>
        /// <param name="name">The name of the route: a non empty name without whitespace.</param>
        /// <param name="target">The target: a string (an URL, the name of a command...).</param>
        public void HostRoute( string name, string target )
        {
            Throw.CheckArgument( IsValidName( name ) );
            Throw.CheckNotNullArgument( target );
            _hostedRoutes[name] = target;
        }

        /// <summary>
        /// Gets or sets the public URL. The "Local:PublicUrl" configuration sets it first: a contributor sets it only
        /// when it is null.
        /// </summary>
        public Uri? PublicUrl { get; set; }

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
        readonly Dictionary<string, PartyRoute> _routes = new( StringComparer.Ordinal );

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
        /// Gets the routes hosted by the party (the target of each route name), stated by the first phase.
        /// </summary>
        public IReadOnlyDictionary<string, string> HostedRoutes => _facts.HostedRoutes;

        /// <summary>
        /// Gets the routes set so far.
        /// </summary>
        public IReadOnlyDictionary<string, PartyRoute> Routes => _routes;

        /// <summary>
        /// Sets (or removes when <paramref name="route"/> is null) a route of the party (see <see cref="PartyDescription.Routes"/>).
        /// </summary>
        /// <param name="name">The name of the route: a non empty name without whitespace.</param>
        /// <param name="route">The route, null to remove it.</param>
        public void SetRoute( string name, PartyRoute? route )
        {
            Throw.CheckArgument( IsValidName( name ) );
            if( route == null ) _routes.Remove( name );
            else _routes[name] = route;
        }

        /// <summary>
        /// Builds the description.
        /// </summary>
        /// <returns>The description.</returns>
        public PartyDescription Build() => new PartyDescription( _facts.PublicUrl, _facts.ClientApplications.ToImmutableArray(), null, _facts.Data, _routes );
    }
}

/// <summary>
/// A client application of a party (see <see cref="PartyDescription.ClientApplications"/>): its name, its request path
/// and its facts.
/// </summary>
public sealed class ClientApplicationDescription : IEquatable<ClientApplicationDescription>
{
    /// <summary>
    /// The context of the <see cref="Facts"/> of the client applications ("PartyClientFact", separator '|'). The facts of a
    /// client come from what it is built from: the PartyClientFact attribute of CK.AppIdentity.PartyClient on its types.
    /// </summary>
    public static readonly CKTraitContext FactContext = CKTraitContext.Create( "PartyClientFact" );

    /// <summary>
    /// Initializes a new client application description.
    /// </summary>
    /// <param name="name">The client application name.</param>
    /// <param name="requestPath">Its request path: "/" for the default one, "/mini-app" for another one.</param>
    /// <param name="facts">Its facts (a trait of the <see cref="FactContext"/>). Null for none.</param>
    public ClientApplicationDescription( string name, string requestPath, CKTrait? facts = null )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckNotNullArgument( requestPath );
        Throw.CheckArgument( facts == null || facts.Context == FactContext );
        Name = name;
        RequestPath = requestPath;
        Facts = facts ?? FactContext.EmptyTrait;
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
    /// CK.AppIdentity.PartyClient). Never null, <see cref="CKTrait.IsEmpty"/> when it has none.
    /// </summary>
    public CKTrait Facts { get; }

    /// <summary>
    /// Gets whether this is the default client application of the party (served at "/").
    /// </summary>
    public bool IsDefault => RequestPath == "/";

    /// <inheritdoc />
    public bool Equals( ClientApplicationDescription? other ) => other != null
                                                                 && Name == other.Name
                                                                 && RequestPath == other.RequestPath
                                                                 && Facts == other.Facts;

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
