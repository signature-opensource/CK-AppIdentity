using CK.AppIdentity;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

[assembly: CK.Setup.AppBuilderContributor( typeof( CK.Setup.AppIdentityAppBuilderContributor ) )]
// The root keys of the central configuration that the contributor reads (see ProjectFromCentral): the address of a link
// is the one of a deployment, each one writes its own.
// ("Domain" and "GlobalDomainPrefix" are keys of the central configuration itself: the domain of the solution.)
[assembly: CentralConfigurationKey( "Environment" )]
[assembly: CentralConfigurationKey( "Links", PerDeployment = ["[*]:Link:Address"] )]
// The address of a remote depends on where it is deployed (in every anchor of the kind "Remote", see DeclareAnchors).
[assembly: ConfigurationPerDeployment( "Remote:Address" )]

namespace CK.Setup;

/// <summary>
/// Reads the party of each App of the builder ("ckonf.json"): its keys (<see cref="ApplicationIdentityServiceConfiguration.RootKeys"/>:
/// "FullName", "Local", "Parties"...) are directly in the App. They are validated at build time
/// (<see cref="ApplicationIdentityServiceConfiguration"/>: an invalid configuration is a build error) and projected in the
/// "CK-AppIdentity" section of the host's "appsettings.json".
/// <list type="bullet">
///   <item>Each local party (the root, the tenant domains) and each client declared by the root party
///   ("Local:PartyClient:{Name}") has its type set (<see cref="TypeSetConfiguration"/> of the App's BinPath): the
///   "Types" and "ExcludedTypes" of its section ("Local" for a party), compile time keys. A party's set is named after
///   the party ("$Front"), a client's after the client ("MiniApp"). The exclusions of a party apply to its clients.</item>
///   <item>The other keys of a client are its features ("NgApp", "Hosting"...). Its build feature ("NgApp") is handled by
///   the contributor of its package (<see cref="DeclaredClient.SetHandled(string)"/>): a client that no contributor builds
///   is an error.</item>
/// </list>
/// </summary>
public sealed class AppIdentityAppBuilderContributor : IAppBuilderContributor
{
    /// <summary>
    /// The section of the host's configuration that holds the party: "CK-AppIdentity".
    /// </summary>
    public const string SectionName = "CK-AppIdentity";

    // The "PartyClient" feature's section (CK.AppIdentity.PartyClient is not referenced by this engine).
    const string PartyClientSectionName = "PartyClient";

    static readonly HashSet<string> _typeSetKeys = new( StringComparer.OrdinalIgnoreCase ) { "Types", "ExcludedTypes" };

    readonly List<DeclaredClient> _clients = new();

    /// <summary>
    /// A client declared by the root party of an App.
    /// </summary>
    public sealed class DeclaredClient
    {
        readonly Dictionary<string, ImmutableConfigurationSection> _features;
        string? _buildFeature;

        internal DeclaredClient( AppConfiguration app, string name, ImmutableConfigurationSection configuration, TypeSetConfiguration typeSet )
        {
            App = app;
            Name = name;
            Configuration = configuration;
            TypeSet = typeSet;
            _features = configuration.GetChildren()
                                     .Where( c => !_typeSetKeys.Contains( c.Key ) )
                                     .ToDictionary( c => c.Key, StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Gets the App whose root party declares this client.
        /// </summary>
        public AppConfiguration App { get; }

        /// <summary>
        /// Gets the client name: its key in "Local:PartyClient".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the configuration of this client ("Local:PartyClient:{Name}" of the App).
        /// </summary>
        public ImmutableConfigurationSection Configuration { get; }

        /// <summary>
        /// Gets the sections of the features of this client by their name (case insensitive): the keys of its section but
        /// its type set ("Types" and "ExcludedTypes").
        /// </summary>
        public IReadOnlyDictionary<string, ImmutableConfigurationSection> Features => _features;

        /// <summary>
        /// Gets the type set of this client (in the BinPath of its App): its "Types" and "ExcludedTypes", with the exclusions
        /// of its party.
        /// </summary>
        public TypeSetConfiguration TypeSet { get; }

        /// <summary>
        /// Gets the path of this client's section relative to its <see cref="AppConfiguration.Configuration"/>: the prefix
        /// of the compile time keys of the client (see <see cref="AppConfiguration.AddCompileKey(string)"/>).
        /// </summary>
        public string RelativePath => $"Local:{PartyClientSectionName}:{Name}";

        /// <summary>
        /// Gets the name of the build feature of this client, once a contributor handled it.
        /// </summary>
        public string? BuildFeature => _buildFeature;

        /// <summary>
        /// Gets whether a contributor handled this client (its build feature).
        /// </summary>
        public bool IsHandled => _buildFeature != null;

        /// <summary>
        /// Called by the contributor that builds this client with its build feature ("NgApp" for instance). A client has
        /// one build feature: a second one is an error.
        /// </summary>
        /// <param name="buildFeature">The build feature.</param>
        /// <returns>True on success, false if another build feature handled this client.</returns>
        public bool SetHandled( string buildFeature )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( buildFeature );
            if( _buildFeature != null && !StringComparer.OrdinalIgnoreCase.Equals( _buildFeature, buildFeature ) ) return false;
            _buildFeature = buildFeature;
            return true;
        }

        /// <inheritdoc />
        public override string ToString() => $"client '{Name}' of {App}";
    }

    /// <summary>
    /// Gets the clients declared by the root party of each App.
    /// </summary>
    public IReadOnlyList<DeclaredClient> Clients => _clients;

    /// <summary>
    /// Projects the parties of the central configuration in the Apps of the AppContext. It reads the domain of the
    /// solution (<see cref="CentralConfiguration.Domain"/>) and handles two root keys:
    /// <list type="bullet">
    ///   <item>"Environment" (defaults to "#Dev"): the "FullName" of an App that doesn't write it is
    ///   "{Domain}/{Party}/{Environment}". The name of an App is its party name ("$Front"), possibly after the segments
    ///   of its domain relative to the domain of the solution ("Customers.$Acme" is "{Domain}/Customers/$Acme").</item>
    ///   <item>"Links": the directed links between the parties, <c>{ "From": "$X", "To": "$Y", "Link": { ... } }</c>:
    ///   "Link" is the remote entry of X for Y (its "PartyName" is Y), appended to the "Parties" of X. A link without its
    ///   reverse is valid; a remote both linked and in the "Parties" of the App is an error. The "Address" of a link is a
    ///   value per deployment (see <see cref="CentralDeployment"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The projection context.</param>
    /// <returns>True on success, false on error.</returns>
    public bool ProjectFromCentral( IActivityMonitor monitor, CentralProjectionContext context )
    {
        // The root keys are claimed by the [assembly: CentralConfigurationKey] of this engine.
        var file = context.Central.File;
        var central = context.Central.Configuration;
        bool success = true;
        var domain = context.Central.Domain;
        if( domain != null && !CoreApplicationIdentity.IsValidDomainName( domain ) )
        {
            monitor.Error( $"Invalid \"Domain\" '{domain}' in '{file}'." );
            success = false;
        }
        var environment = central["Environment"] ?? CoreApplicationIdentity.DefaultEnvironmentName;
        if( !CoreApplicationIdentity.IsValidEnvironmentName( environment ) )
        {
            monitor.Error( $"Invalid \"Environment\" '{environment}' in '{file}' (\"#Dev\" by default: it starts with a '#')." );
            success = false;
        }
        if( !success ) return false;
        // The party of each App: its name is its party name, after the segments of its domain relative to the "Domain".
        var parties = new Dictionary<string, CentralApp>( StringComparer.OrdinalIgnoreCase );
        var appDomains = new Dictionary<CentralApp, string?>();
        foreach( var a in context.Central.AllApps )
        {
            if( !CheckAppName( monitor, file, a, domain, out var nameParty, out var appDomain ) )
            {
                success = false;
                continue;
            }
            appDomains.Add( a, appDomain );
            // A fleet App (a template: "InstanceCount" or "Instances") names its party with a placeholder: "$Trolley-{Name}".
            var partyName = RawPartyName( a.Configuration )
                            ?? (ConfigurationTemplate.IsTemplate( a.Configuration ) ? $"{nameParty}-{{Name}}" : nameParty);
            if( !parties.TryAdd( partyName, a ) )
            {
                monitor.Error( $"The party '{partyName}' of {a} is also the party of {parties[partyName]} in '{file}'." );
                success = false;
            }
        }
        var links = ReadLinks( monitor, context.Central, parties, ref success );
        success &= CheckTenantTemplates( monitor, context.Central, domain, appDomains );
        if( !success ) return false;
        foreach( var a in context.Apps )
        {
            var partyName = parties.First( kv => kv.Value == a.App ).Key;
            if( appDomains[a.App] is string appDomain && !a.Node.ContainsKey( "FullName" ) )
            {
                a.Node.Insert( 0, "FullName", $"{appDomain}/{partyName}/{environment}" );
            }
            var outgoing = links.Where( l => l.From.Equals( partyName, StringComparison.OrdinalIgnoreCase ) ).ToList();
            if( outgoing.Count == 0 ) continue;
            if( a.Node["Parties"] is not JsonArray remotes )
            {
                if( a.Node.ContainsKey( "Parties" ) )
                {
                    monitor.Error( $"\"Parties\" of {a.App} in '{file}' must be an array." );
                    success = false;
                    continue;
                }
                remotes = new JsonArray();
                a.Node["Parties"] = remotes;
            }
            foreach( var l in outgoing )
            {
                if( remotes.OfType<JsonObject>().Any( r => r["PartyName"] is JsonValue v
                                                           && v.TryGetValue<string>( out var n )
                                                           && n.Equals( l.To, StringComparison.OrdinalIgnoreCase ) ) )
                {
                    monitor.Error( $"The remote '{l.To}' of {a.App} is linked (\"Links\" from '{l.From}' to '{l.To}') and in its \"Parties\": in '{file}', it must be one or the other." );
                    success = false;
                    continue;
                }
                var entry = new JsonObject { ["PartyName"] = l.To };
                // A link to a fleet is a remote template: one remote per instance of the fleet.
                var target = parties[l.To].Configuration;
                if( target[ConfigurationTemplate.InstanceCountKey] is string count )
                {
                    entry[ConfigurationTemplate.InstanceCountKey] = int.Parse( count, System.Globalization.CultureInfo.InvariantCulture );
                }
                else if( target.TryGetSection( ConfigurationTemplate.InstancesKey ) is { } names )
                {
                    entry[ConfigurationTemplate.InstancesKey] = new JsonArray( names.GetChildren().Select( n => (JsonNode?)JsonValue.Create( n.Value ) ).ToArray() );
                }
                // A link to an Opened fleet is an Opened remote template: its dynamic instances are accepted (by approval).
                if( target[ConfigurationTemplate.MaxInstanceCountKey] is string max )
                {
                    entry[ConfigurationTemplate.MaxInstanceCountKey] = int.Parse( max, System.Globalization.CultureInfo.InvariantCulture );
                }
                if( l.Link != null )
                {
                    foreach( var (key, value) in l.Link ) entry[key] = value?.DeepClone();
                }
                remotes.Add( entry );
            }
        }
        return success;

        // The tenant templates of the System (the central configuration sees all its Apps):
        //  - In a fleet App, each process has its own tenants: the names of a tenant template use the placeholder of the
        //    App ("{Trolley.Name}" or "{Trolley.Index}"), otherwise the processes of the fleet would create the same tenants.
        //  - An Opened tenant template owns its domain prefix: its run-time tenants are named by their creator, so no other
        //    party of the System has its domain directly under it, and no other tenant template has the same prefix.
        static bool CheckTenantTemplates( IActivityMonitor monitor, CentralConfiguration central, string? domain, Dictionary<CentralApp, string?> appDomains )
        {
            bool success = true;
            var file = central.File;
            var prefixes = new List<(string Prefix, string Path)>();
            var domains = new List<(string Domain, string Path)>();
            if( domain != null ) domains.Add( (domain, "Domain") );
            foreach( var a in central.AllApps )
            {
                // The domain of the App: the one of its "FullName", or the one its name derives.
                if( DomainOf( a.Configuration["FullName"] ) is string d ) domains.Add( (d, $"{a.Configuration.Path}:FullName") );
                else if( appDomains.GetValueOrDefault( a ) is string derived ) domains.Add( (derived, $"{a.Configuration.Path} ({a})") );
                Collect( monitor, file, a, ConfigurationTemplate.IsTemplate( a.Configuration ), a.Configuration.GetSection( "Parties" ), prefixes, domains, ref success );
            }
            for( int i = 0; i < prefixes.Count; ++i )
            {
                var (prefix, path) = prefixes[i];
                for( int j = i + 1; j < prefixes.Count; ++j )
                {
                    if( prefixes[j].Prefix.Equals( prefix, StringComparison.OrdinalIgnoreCase ) )
                    {
                        monitor.Error( $"The Opened tenant templates '{path}' and '{prefixes[j].Path}' of '{file}' have the same domain prefix '{prefix}': a tenant template owns its prefix." );
                        success = false;
                    }
                }
                foreach( var (d, dPath) in domains )
                {
                    if( d.Length > prefix.Length + 1
                        && d.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )
                        && d[prefix.Length] == '/'
                        && d.IndexOf( '/', prefix.Length + 1 ) < 0 )
                    {
                        monitor.Error( $"The domain '{d}' of '{dPath}' is under the domain prefix '{prefix}' of the Opened tenant template '{path}' in '{file}': a tenant template owns its prefix (its run-time tenants are named by their creator)." );
                        success = false;
                    }
                }
            }
            return success;

            static void Collect( IActivityMonitor monitor,
                                 string file,
                                 CentralApp app,
                                 bool isFleet,
                                 Microsoft.Extensions.Configuration.IConfigurationSection parties,
                                 List<(string Prefix, string Path)> prefixes,
                                 List<(string Domain, string Path)> domains,
                                 ref bool success )
            {
                foreach( var p in parties.GetChildren() )
                {
                    if( ConfigurationTemplate.IsTemplate( p ) && TenantDomainTemplate.IsTenantTemplate( p ) )
                    {
                        var names = $"{p["FullName"]} {p["DomainName"]}";
                        var placeholder = PlaceholderName( app.Name );
                        if( isFleet
                            && names.IndexOf( $"{placeholder}.Name}}", StringComparison.OrdinalIgnoreCase ) < 0
                            && names.IndexOf( $"{placeholder}.Index}}", StringComparison.OrdinalIgnoreCase ) < 0 )
                        {
                            monitor.Error( $"The tenant template '{p.Path}' of the fleet {app} in '{file}' must use the placeholder of its App in its names (\"{{{placeholder}.Name}}\" or \"{{{placeholder}.Index}}\"): each process of the fleet has its own tenants." );
                            success = false;
                        }
                        var raw = p["DomainName"];
                        if( p[ConfigurationTemplate.MaxInstanceCountKey] != null
                            && raw != null
                            && raw.EndsWith( "/{Name}", StringComparison.OrdinalIgnoreCase ) )
                        {
                            prefixes.Add( (raw.Substring( 0, raw.Length - 7 ), p.Path) );
                        }
                        continue;
                    }
                    if( (p["DomainName"] ?? DomainOf( p["FullName"] )) is string d ) domains.Add( (d, p.Path) );
                    Collect( monitor, file, app, isFleet, p.GetSection( "Parties" ), prefixes, domains, ref success );
                }
            }
        }

        // The domain of a full name, written with or without its environment (null when there is no "$Party" part).
        static string? DomainOf( string? fullName )
        {
            if( fullName == null ) return null;
            var parts = fullName.Split( '/' );
            int iParty = Array.FindIndex( parts, p => p.StartsWith( '$' ) );
            return iParty > 0 ? string.Join( '/', parts, 0, iParty ) : null;
        }

        static List<(string From, string To, JsonObject? Link)> ReadLinks( IActivityMonitor monitor,
                                                                           CentralConfiguration central,
                                                                           Dictionary<string, CentralApp> parties,
                                                                           ref bool success )
        {
            var result = new List<(string From, string To, JsonObject? Link)>();
            var file = central.File;
            var json = central.GetJson( "Links" );
            if( json == null ) return result;
            if( json is not JsonArray links )
            {
                monitor.Error( $"\"Links\" in '{file}' must be an array of {{ \"From\": \"$X\", \"To\": \"$Y\", \"Link\": {{ ... }} }}." );
                success = false;
                return result;
            }
            for( int i = 0; i < links.Count; ++i )
            {
                var path = $"Links:{i}";
                if( links[i] is not JsonObject l )
                {
                    monitor.Error( $"\"{path}\" in '{file}' must be an object: {{ \"From\": \"$X\", \"To\": \"$Y\", \"Link\": {{ ... }} }}." );
                    success = false;
                    continue;
                }
                bool ok = true;
                foreach( var (key, _) in l )
                {
                    if( !(key.Equals( "From", StringComparison.OrdinalIgnoreCase )
                          || key.Equals( "To", StringComparison.OrdinalIgnoreCase )
                          || key.Equals( "Link", StringComparison.OrdinalIgnoreCase )) )
                    {
                        monitor.Error( $"Unknown key \"{path}:{key}\" in '{file}': a link has \"From\", \"To\" and \"Link\" (the remote entry of From for To)." );
                        ok = false;
                    }
                }
                var from = ReadParty( monitor, file, path, l, "From", parties, ref ok );
                var to = ReadParty( monitor, file, path, l, "To", parties, ref ok );
                JsonObject? link = null;
                var linkNode = l["Link"];
                if( linkNode != null )
                {
                    link = linkNode as JsonObject;
                    if( link == null )
                    {
                        monitor.Error( $"\"{path}:Link\" in '{file}' must be an object: the remote entry of From for To." );
                        ok = false;
                    }
                    else if( link.ContainsKey( "PartyName" ) )
                    {
                        monitor.Error( $"\"{path}:Link\" in '{file}' can't have a \"PartyName\": it is the party \"To\"." );
                        ok = false;
                    }
                }
                if( !ok )
                {
                    success = false;
                    continue;
                }
                if( from!.Equals( to, StringComparison.OrdinalIgnoreCase ) )
                {
                    monitor.Error( $"The link \"{path}\" in '{file}' links '{from}' to itself." );
                    success = false;
                    continue;
                }
                if( result.Any( r => r.From.Equals( from, StringComparison.OrdinalIgnoreCase ) && r.To.Equals( to, StringComparison.OrdinalIgnoreCase ) ) )
                {
                    monitor.Error( $"The link from '{from}' to '{to}' is defined twice in '{file}'." );
                    success = false;
                    continue;
                }
                result.Add( (from, to!, link) );
            }
            return result;
        }

        static string? ReadParty( IActivityMonitor monitor,
                                  NormalizedPath file,
                                  string path,
                                  JsonObject link,
                                  string key,
                                  Dictionary<string, CentralApp> parties,
                                  ref bool ok )
        {
            var name = link[key] is JsonValue v && v.TryGetValue<string>( out var s ) ? s : null;
            // The party of a fleet is its pattern ("$Trolley-{Name}").
            if( name == null || !name.StartsWith( '$' ) || !(CoreApplicationIdentity.IsValidPartyName( name ) || parties.ContainsKey( name )) )
            {
                monitor.Error( $"Invalid \"{path}:{key}\" in '{file}': a party name (\"$Name\") is expected." );
                ok = false;
                return null;
            }
            if( !parties.ContainsKey( name ) )
            {
                monitor.Error( $"The party '{name}' of \"{path}:{key}\" in '{file}' is not a party of the central configuration (its parties are: '{parties.Keys.Order().Concatenate( "', '" )}')." );
                ok = false;
                return null;
            }
            // The name as the party is named.
            return parties.Keys.First( k => k.Equals( name, StringComparison.OrdinalIgnoreCase ) );
        }
    }

    /// <inheritdoc />
    public bool Contribute( IActivityMonitor monitor, AppBuilderContext context )
    {
        bool success = true;
        foreach( var a in context.Apps )
        {
            // The keys of the party are the ones of the root of an AppIdentity configuration: an App without any of them
            // is not a party.
            var keys = a.Configuration.GetChildren().Where( c => ApplicationIdentityServiceConfiguration.RootKeys.Contains( c.Key ) ).ToList();
            if( keys.Count == 0 ) continue;
            // The configuration of the party alone (the other keys of the App are not its own), with the paths of the
            // "ckonf.json" (for the errors).
            var party = new MutableConfigurationSection( a.Configuration.Path );
            foreach( var k in keys )
            {
                a.ProjectInSection( k.Key, SectionName );
                Copy( party, k.Key, k );
            }
            var section = new ImmutableConfigurationSection( party );
            // A fleet App ("InstanceCount", "Instances" or "MaxInstanceCount"): its party is validated for each instance
            // (and the first dynamic one of an Opened fleet), its placeholders resolved; the instances share the type sets,
            // named after the templates ("$Trolley-{Name}").
            if( !ConfigurationTemplate.TryReadInstances( monitor, a.Configuration, PlaceholderName( a.Name ), out var instances ) )
            {
                success = false;
                continue;
            }
            string? rootSetName = null;
            var checkedInstances = CheckedInstances( instances );
            if( checkedInstances != null )
            {
                rootSetName = RawPartyName( section ) ?? $"{PartyOf( a.Name )}-{{Name}}";
                if( checkedInstances.Count == 0
                    || !ConfigurationTemplate.TryResolve( monitor, rootSetName, section.Path, checkedInstances[0], [], out var r )
                    || r == rootSetName )
                {
                    monitor.Error( $"The party name '{rootSetName}' of the fleet {a} must use the \"{{Name}}\" or \"{{Index}}\" placeholder: its instances are different parties." );
                    success = false;
                    continue;
                }
            }
            ApplicationIdentityServiceConfiguration? configuration = null;
            using( monitor.OpenInfo( checkedInstances == null ? $"Checking the party of {a}." : $"Checking the {checkedInstances.Count} instances of the party of {a}." ) )
            {
                foreach( var i in (IEnumerable<ConfigurationInstance?>?)checkedInstances ?? [null] )
                {
                    var s = section;
                    var defaultPartyName = PartyOf( a.Name ).TrimStart( '$' );
                    if( i != null )
                    {
                        s = ConfigurationTemplate.Instantiate( monitor, section, i );
                        ConfigurationTemplate.TryResolve( monitor, rootSetName!, section.Path, i, [], out defaultPartyName );
                        defaultPartyName = defaultPartyName.TrimStart( '$' );
                    }
                    // The relative paths (StoreRootPath) are the host's: its content root is its project folder.
                    var c = s != null
                            ? ApplicationIdentityServiceConfiguration.Create( monitor, s, defaultPartyName: defaultPartyName, basePath: a.HostProjectPath )
                            : null;
                    if( c == null )
                    {
                        monitor.Error( i == null ? $"Invalid party in {a}." : $"Invalid party in {a} for its {i}." );
                        success = false;
                    }
                    configuration ??= c;
                }
            }
            if( configuration == null ) continue;
            // The configuration levels: the keys of the party are read at start (Init), a "$LOCK" can restrict them; the
            // parties are the anchors of the "$LOCK" and of the packages' restrictions (their values may come from the
            // "Defaults" above them).
            foreach( var k in keys ) a.DeclareInitSection( k.Key );
            DeclareAnchors( a, configuration );
            // A host built for a deployment burns the full name of its party: CK.AppHost reads its environment before
            // anything else (the .NET environment of the host follows it).
            if( context.Deployment != null && a.Configuration["FullName"] != null )
            {
                a.AddLock( new ConfigurationLockEntry( a.Configuration.Path, "FullName", nameof( ConfigurationLevel.Compile ) ) );
            }
            // The type sets of the local parties (the ones of the first instance: the instances share them).
            var root = CreateTypeSet( monitor, a, rootSetName ?? configuration.PartyName, configuration.LocalConfiguration.Configuration, null, ref success );
            var tenantSets = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
            foreach( var t in configuration.TenantDomains )
            {
                // The instances of a tenant template share the set of the template, named by its unresolved party name.
                var name = TemplateSetName( section, t.Configuration ) ?? t.PartyName;
                if( tenantSets.Add( name ) ) CreateTypeSet( monitor, a, name, t.LocalConfiguration.Configuration, null, ref success );
            }
            var clients = section.TryGetSection( "Local" )?.TryGetSection( PartyClientSectionName );
            if( clients == null || root == null ) continue;
            foreach( var c in clients.GetChildren() )
            {
                var set = CreateTypeSet( monitor, a, c.Key, c, root, ref success );
                if( set != null ) _clients.Add( new DeclaredClient( a, c.Key, c, set ) );
                // A client is the anchor of the restrictions of the client features and their options.
                a.DeclareAnchor( $"Local:{PartyClientSectionName}:{c.Key}", ["Client"] );
            }
        }
        return success;

        // In the order of the configuration (the order of the declared clients is the one of the file).
        static void Copy( MutableConfigurationSection target, string path, ImmutableConfigurationSection s )
        {
            if( s.Value != null ) target[path] = s.Value;
            foreach( var c in s.GetChildren() ) Copy( target, $"{path}:{c.Key}", c );
        }

        // The unresolved party name of the template of a party (its section has the path of the template's), or null when
        // it is not the instance of a template.
        static string? TemplateSetName( ImmutableConfigurationSection unresolved, ImmutableConfigurationSection party )
        {
            var prefix = unresolved.Path + ":";
            if( !party.Path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ) return null;
            var template = unresolved.TryGetSection( party.Path[prefix.Length..] );
            if( template == null || !ConfigurationTemplate.IsTemplate( template ) ) return null;
            return RawPartyName( template );
        }
    }

    // The parties of the App, by their kinds: "Party" and "Local" (a local party: the root's or a tenant's), "TenantDomain",
    // "TenantTemplate", "Remote", "RemoteTemplate", "OpenedRemoteTemplate". The instances of a template are configured by
    // the template: the template is the anchor.
    static void DeclareAnchors( AppConfiguration a, ApplicationIdentityServiceConfiguration c )
    {
        Declare( a, c.LocalConfiguration.Configuration.Path, "Party", "Local" );
        DeclareRemotes( a, c.Remotes, c.RemoteTemplates );
        foreach( var t in c.TenantDomains )
        {
            if( t.Template != null )
            {
                Declare( a, $"{t.Template.Configuration.Path}:Local", "Party", "Local", "TenantDomain", "TenantTemplate" );
            }
            else
            {
                Declare( a, t.LocalConfiguration.Configuration.Path, "Party", "Local", "TenantDomain" );
            }
            DeclareRemotes( a, t.Remotes, t.RemoteTemplates );
        }
        foreach( var t in c.TenantTemplates )
        {
            Declare( a, $"{t.Configuration.Path}:Local", "Party", "Local", "TenantDomain", "TenantTemplate" );
        }

        static void DeclareRemotes( AppConfiguration a, IEnumerable<RemotePartyConfiguration> remotes, IEnumerable<RemotePartyTemplate> opened )
        {
            foreach( var r in remotes )
            {
                if( r.Template == null ) Declare( a, r.Configuration.Path, "Party", "Remote" );
                else if( r.Template.Instances.IsOpened ) Declare( a, r.Template.Configuration.Path, "Party", "Remote", "RemoteTemplate", "OpenedRemoteTemplate" );
                else Declare( a, r.Template.Configuration.Path, "Party", "Remote", "RemoteTemplate" );
            }
            foreach( var t in opened )
            {
                Declare( a, t.Configuration.Path, "Party", "Remote", "RemoteTemplate", "OpenedRemoteTemplate" );
            }
        }

        static void Declare( AppConfiguration a, string path, params string[] kinds )
        {
            var prefix = a.Configuration.Path + ":";
            if( !path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ) return;
            var relative = path[prefix.Length..];
            a.DeclareAnchor( relative, kinds, Fallbacks( a, relative ) );
        }

        // The "Defaults" of the levels above the party, the nearest first.
        static IEnumerable<string> Fallbacks( AppConfiguration a, string relative )
        {
            var parts = relative.Split( ':' );
            var result = new List<string>();
            for( int n = parts.Length - 1; n >= 0; --n )
            {
                var level = string.Join( ':', parts, 0, n );
                var defaults = level.Length == 0 ? "Defaults" : $"{level}:Defaults";
                if( a.Configuration.TryGetSection( defaults ) != null ) result.Add( defaults );
            }
            return result;
        }
    }

    // The instances of a fleet that are checked: the configured ones and, for an Opened fleet, its first dynamic one (the
    // dynamic instances differ only by their index). Null when the App is not a fleet.
    internal static IReadOnlyList<ConfigurationInstance>? CheckedInstances( ConfigurationTemplateInstances? instances )
    {
        if( instances == null ) return null;
        if( !instances.IsOpened || instances.MaxInstanceCount == instances.Count ) return instances;
        return instances.Append( instances.CreateDynamic( instances.Count + 1 ) ).ToList();
    }

    // The party name written by a party section: its "PartyName", or the party of its "FullName" (a template's name
    // has placeholders: it is not parsed as a full name).
    /// <summary>
    /// The root key of the central configuration that holds the prefix of its "Domain" that the names of the projects of
    /// the Apps leave out (see <see cref="CentralConfiguration.GlobalDomainPrefixKey"/>).
    /// </summary>
    public const string GlobalDomainPrefixKey = CentralConfiguration.GlobalDomainPrefixKey;

    // The party of an App: the last segment of its name ("Customers.$Acme" -> "$Acme").
    static string PartyOf( string appName ) => appName.Split( '.' )[^1];

    // The placeholder of a fleet App in its templates: its party without the '$' ("{Worker.Name}").
    static string PlaceholderName( string appName ) => PartyOf( appName ).TrimStart( '$' );

    // The name of an App of a central configuration is its party name ("$Front"), possibly after the segments of its
    // domain relative to the "Domain" ("Customers.$Acme": "{Domain}/Customers"). An explicit party name ("PartyName",
    // "FullName") must be its party (a fleet: "$Party-{Name}"), the domain of an explicit "FullName" must be its domain.
    // Without "Domain" and without segments, the domain of the App is the one of its "FullName" (null without it).
    static bool CheckAppName( IActivityMonitor monitor, NormalizedPath file, CentralApp a, string? domain, out string party, out string? appDomain )
    {
        var parts = a.Name.Split( '.' );
        party = parts[^1];
        var segments = string.Join( '/', parts[..^1] );
        var fullName = a.Configuration["FullName"];
        var explicitDomain = fullName != null ? string.Join( '/', fullName.Split( '/' ).TakeWhile( p => !p.StartsWith( '$' ) ) ) : null;
        appDomain = domain != null
                        ? (segments.Length == 0 ? domain : $"{domain}/{segments}")
                        : (segments.Length == 0 ? explicitDomain : segments);
        var raw = RawPartyName( a.Configuration );
        if( (explicitDomain != null && !explicitDomain.Equals( appDomain, StringComparison.OrdinalIgnoreCase ))
            || (raw != null && !raw.Equals( party, StringComparison.OrdinalIgnoreCase )
                            && !raw.StartsWith( party + "-{", StringComparison.OrdinalIgnoreCase )) )
        {
            monitor.Error( $"The \"FullName\" of {a} in '{file}' must be its party '{party}' in its domain '{appDomain}' (the name of an App is its party name, after the segments of its domain relative to the \"Domain\")." );
            return false;
        }
        return true;
    }

    static string? RawPartyName( IConfiguration s )
    {
        var p = s["PartyName"];
        if( !string.IsNullOrWhiteSpace( p ) ) return p[0] == '$' ? p : '$' + p;
        return s["FullName"]?.Split( '/' ).FirstOrDefault( part => part.StartsWith( '$' ) );
    }

    // The type set of a local party (its "Local" section) or of a client (its section, the set of its party as parent):
    // added to the BinPath of the App; its keys are compile time keys.
    static TypeSetConfiguration? CreateTypeSet( IActivityMonitor monitor,
                                                AppConfiguration a,
                                                string name,
                                                ImmutableConfigurationSection s,
                                                TypeSetConfiguration? parent,
                                                ref bool success )
    {
        var set = new TypeSetConfiguration( name );
        bool ok = true;
        foreach( var key in _typeSetKeys )
        {
            var list = s.TryGetSection( key );
            if( list == null ) continue;
            a.AddCompileKey( RelativePath( a, list ) );
            var target = key.Equals( "Types", StringComparison.OrdinalIgnoreCase ) ? set.Types : set.ExcludedTypes;
            // A string, or an object whose "Name" is the type (the other keys are the projection's: "Optional"...).
            IEnumerable<ImmutableConfigurationSection> items = list.Value != null ? new[] { list } : list.GetChildren();
            foreach( var item in items )
            {
                var typeName = item.Value ?? item["Name"];
                if( string.IsNullOrWhiteSpace( typeName ) )
                {
                    monitor.Error( $"\"{item.Path}\" must be a type name or an object with a \"Name\"." );
                    ok = false;
                    continue;
                }
                var type = SimpleTypeFinder.WeakResolver( typeName, throwOnError: false );
                if( type == null )
                {
                    monitor.Error( $"Unable to find the type '{typeName}' of \"{item.Path}\"." );
                    ok = false;
                    continue;
                }
                target.Add( type );
            }
        }
        if( parent != null )
        {
            // The exclusions apply downward.
            foreach( var t in set.Types.Where( parent.ExcludedTypes.Contains ) )
            {
                monitor.Error( $"The client '{name}' of {a} adds the type '{t:N}' that its party excludes: the exclusions apply downward." );
                ok = false;
            }
            set.ExcludedTypes.AddRange( parent.ExcludedTypes );
        }
        if( !ok )
        {
            success = false;
            return null;
        }
        if( a.BinPathConfiguration.FindTypeSet( name ) != null )
        {
            monitor.Error( $"The type set '{name}' is defined twice in {a}: a client can't have the name of a party." );
            success = false;
            return null;
        }
        a.BinPathConfiguration.AddTypeSet( set );
        return set;

        static string RelativePath( AppConfiguration a, ImmutableConfigurationSection s )
        {
            var prefix = a.Configuration.Path + ":";
            return s.Path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ? s.Path[prefix.Length..] : s.Path;
        }
    }

    /// <summary>
    /// Resolves the sections of an option (<see cref="IPartyComponentOption{TOwner}"/>): the section of the option in the
    /// one of each concrete owner of the App ("HttpEndpoint:ForwardedHeaders"), relative to a party for the options of
    /// a feature, to a client for the options of a client feature. The features and the client features are their
    /// configurable name (null: the default).
    /// </summary>
    /// <param name="configurableType">The configurable type.</param>
    /// <param name="appTypes">The configurable types of the App.</param>
    /// <returns>The sections of an option, null for another type.</returns>
    public IEnumerable<string>? ResolveSections( Type configurableType, IReadOnlyCollection<Type> appTypes )
    {
        var owner = configurableType.GetInterfaces()
                                    .Where( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IPartyComponentOption<> ) )
                                    .Select( i => i.GetGenericArguments()[0] )
                                    .FirstOrDefault();
        if( owner == null ) return null;
        var name = configurableType.GetCustomAttributes( inherit: false ).OfType<IPartyComponentOptionAttribute>().FirstOrDefault()?.Name;
        if( name == null ) return null;
        // An option of a base owner applies to every concrete owner.
        return appTypes.Where( t => t.IsClass && !t.IsAbstract && owner.IsAssignableFrom( t ) )
                       .Select( t => t.GetCustomAttributes( inherit: false ).OfType<IConfigurableTypeAttribute>().FirstOrDefault()?.Name )
                       .Where( n => n != null )
                       .Select( n => $"{n}:{name}" )
                       .Distinct( StringComparer.OrdinalIgnoreCase )
                       .ToList();
    }

    /// <inheritdoc />
    public bool OnContributed( IActivityMonitor monitor, AppBuilderContext context )
    {
        bool success = true;
        foreach( var c in _clients.Where( c => !c.IsHandled ) )
        {
            monitor.Error( $"No package builds the {c}: its build feature ('NgApp' for an Angular application, \"NgApp\": true without configuration: an empty object is no declaration) is missing, or its package is not referenced by the builder." );
            success = false;
        }
        return success;
    }
}
