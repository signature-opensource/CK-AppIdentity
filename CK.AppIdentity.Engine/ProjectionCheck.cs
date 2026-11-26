using CK.AppIdentity;
using CK.Core;
using CK.TypeCache;
using CK.TypeSet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// The build time checks of the projection of the party (the "CK-AppIdentity" section of the host's "appsettings.json",
/// written by the App builder before the engine runs). Without a projection (a test, a "CKSetup.xml"), there is nothing
/// to check.
/// <list type="bullet">
///   <item>
///   The types drive: a key that declares a party component must name a member of the type set of its party (or of its
///   client); an option enabled in a feature's section must be a member too. A key doesn't bring its type in.
///   <c>false</c> is fine, an unknown key is left to the run time (a warning).
///   </item>
///   <item>
///   The scopes: the drivers that a local party has (its declared members and the ones that are not optional and not
///   turned off) and the features that a client has (the same, with the drivers of its party as its outer scope) follow
///   the rules of <see cref="PartyComponentDirectory.CheckScope(IActivityMonitor, string, IReadOnlyCollection{PartyComponentInfo}, Func{Type, bool}, Func{Type, bool})"/>:
///   a required component or abstraction is there, a unique abstraction is implemented at most once. The uniqueness
///   applies to the declared scope, not to the set (a set that nothing restricts has all the implementations).
///   </item>
/// </list>
/// </summary>
static class ProjectionCheck
{
    const string PartyClientSectionName = "PartyClient";

    sealed record Option( string Name, Type Owner, Type Type, IReadOnlyList<Type> RequiredFeatures );

    // The projected JSON with the burnt values (their configuration paths) set in it.
    static string WithCompiledValues( string json, IReadOnlyDictionary<string, string?> compiled )
    {
        if( compiled.Count == 0 ) return json;
        var options = new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true };
        if( System.Text.Json.Nodes.JsonNode.Parse( json, documentOptions: options ) is not System.Text.Json.Nodes.JsonObject root ) return json;
        foreach( var (path, value) in compiled )
        {
            System.Text.Json.Nodes.JsonNode current = root;
            var segments = path.Split( ':' );
            for( int i = 0; i < segments.Length; ++i )
            {
                bool last = i == segments.Length - 1;
                var key = segments[i];
                if( current is System.Text.Json.Nodes.JsonArray a && int.TryParse( key, out var index ) && index >= 0 && index < a.Count )
                {
                    if( last ) a[index] = value;
                    else current = a[index] ??= new System.Text.Json.Nodes.JsonObject();
                }
                else if( current is System.Text.Json.Nodes.JsonObject o )
                {
                    if( last ) o[key] = value;
                    else
                    {
                        var next = o[key];
                        if( next is not (System.Text.Json.Nodes.JsonObject or System.Text.Json.Nodes.JsonArray) ) o[key] = next = new System.Text.Json.Nodes.JsonObject();
                        current = next!;
                    }
                }
                else break;
            }
        }
        return root.ToJsonString();
    }

    public static bool Run( IActivityMonitor monitor, IRunningBinPathGroup group, PartyComponentAnalysis.Result result )
    {
        if( group.TypeSets.Count == 0 ) return true;
        bool success = true;
        foreach( var b in group.SimilarConfigurations )
        {
            if( b.ProjectPath.IsEmptyPath ) continue;
            var file = b.ProjectPath.AppendPart( "appsettings.json" );
            if( !File.Exists( file ) ) continue;
            // The burnt values (the Compile level: a "$LOCK", the full name of a party built for a deployment) are not in
            // the projection: they are part of the configuration of the host at run time.
            var appSettings = ImmutableConfigurationSection.CreateFromJson( "appsettings", WithCompiledValues( File.ReadAllText( file ), b.CompiledValues ) );
            if( appSettings.TryGetSection( AppIdentityAppBuilderContributor.SectionName ) == null ) continue;
            // A fleet (a template: "InstanceCount", "Instances" or "MaxInstanceCount"): each instance is checked (and the
            // first dynamic one of an Opened fleet), its placeholders resolved (the App name is the host's without ".Host":
            // the qualifier of its placeholders).
            var appName = b.ProjectPath.LastPart;
            if( appName.EndsWith( ".Host", StringComparison.OrdinalIgnoreCase ) ) appName = appName[..^5];
            if( !ConfigurationTemplate.TryReadInstances( monitor, appSettings, appName, out var instances ) ) return false;
            foreach( var i in (IEnumerable<ConfigurationInstance?>?)AppIdentityAppBuilderContributor.CheckedInstances( instances ) ?? [null] )
            {
                var s = i == null ? appSettings : ConfigurationTemplate.Instantiate( monitor, appSettings, i );
                var section = s?.TryGetSection( AppIdentityAppBuilderContributor.SectionName );
                if( section == null ) return false;
                using( monitor.OpenTrace( $"Checking the type sets of the party projected in '{file}'{(i != null ? $" for its {i}" : "")}." ) )
                {
                    var c = ApplicationIdentityServiceConfiguration.Create( monitor, section );
                    if( c == null ) return false;
                    success &= Check( monitor, group, result, c );
                }
            }
        }
        return success;
    }

    static bool Check( IActivityMonitor monitor, IRunningBinPathGroup group, PartyComponentAnalysis.Result result, ApplicationIdentityServiceConfiguration c )
    {
        var sets = group.TypeSets.ToDictionary( kv => kv.Key, kv => kv.Value.Select( t => t.Type ).ToHashSet(), StringComparer.OrdinalIgnoreCase );
        var drivers = result.Components.Where( i => typeof( IApplicationIdentityFeatureDriver ).IsAssignableFrom( i.ComponentType ) ).ToList();
        var clientFeatures = result.Components.Where( i => !typeof( IApplicationIdentityFeatureDriver ).IsAssignableFrom( i.ComponentType ) ).ToList();
        var options = CollectOptions( monitor, group );
        if( options == null ) return false;
        var partyDrivers = new Dictionary<string, HashSet<Type>>( StringComparer.OrdinalIgnoreCase );
        bool success = true;

        CheckParty( c.PartyName, c.LocalConfiguration, c.Remotes );
        // A tenant domain of a template uses the set of its template (as at run time: see ApplicationIdentityFeatureDriver).
        foreach( var t in c.TenantDomains ) CheckParty( t.Template?.PartyNamePattern ?? t.PartyName, t.LocalConfiguration, t.Remotes );
        // The clients are declared by the root party only.
        if( c.LocalConfiguration.Configuration.TryGetSection( PartyClientSectionName ) is { } clients )
        {
            var outer = partyDrivers[c.PartyName];
            foreach( var client in clients.GetChildren() )
            {
                var scope = clientFeatures.Where( f => IsMember( client.Key, f.ComponentType ) && IsActive( f, client.TryGetSection( f.FeatureName ) ) ).ToList();
                success &= PartyComponentDirectory.CheckScope( monitor, $"the client '{client.Key}'", scope, result.MultipleAbstractions.Contains, outer.Contains );
                // An option of a client feature may require a party feature or another feature of the client.
                var active = outer.Concat( scope.Select( f => f.ComponentType ) ).ToHashSet();
                foreach( var k in client.GetChildren() )
                {
                    if( TypeSetConfiguration.Keys.Contains( k.Key ) || IsFalse( k ) ) continue;
                    var f = clientFeatures.FirstOrDefault( i => string.Equals( i.FeatureName, k.Key, StringComparison.OrdinalIgnoreCase ) );
                    if( f == null ) continue;
                    if( !IsMember( client.Key, f.ComponentType ) )
                    {
                        Error( k.Path, $"the client feature '{f.ComponentType:N}' is not a member of the type set of the client '{client.Key}'" );
                    }
                    else if( k.Value == null )
                    {
                        CheckOptions( f.ComponentType, client.Key, name => k.TryGetSection( name ), active );
                    }
                }
            }
        }
        return success;

        void CheckParty( string partyName, ApplicationIdentityConfiguration local, IEnumerable<ApplicationIdentityConfiguration> remotes )
        {
            var scope = drivers.Where( d => IsMember( partyName, d.ComponentType ) && IsActive( d, local.GetFeatureDeclarationSection( d.FeatureName ) ) ).ToList();
            partyDrivers[partyName] = scope.Select( d => d.ComponentType ).ToHashSet();
            CheckKeys( partyName, local, isLocal: true );
            foreach( var r in remotes ) CheckKeys( partyName, r, isLocal: false );
            success &= PartyComponentDirectory.CheckScope( monitor, $"the party '{partyName}'", scope, result.MultipleAbstractions.Contains, _ => false );
        }

        // The keys of a party (and of the "Defaults" above it): the drivers that read a key; it is fine when one of them
        // is a member (the two sides of an asymmetric feature read "XXXRemote").
        void CheckKeys( string setName, ApplicationIdentityConfiguration p, bool isLocal )
        {
            foreach( var key in p.GetFeatureKeys() )
            {
                var readers = drivers.Where( d => string.Equals( ConfigurationKey( d, isLocal ), key, StringComparison.OrdinalIgnoreCase ) ).ToList();
                if( readers.Count == 0 ) continue;
                var declaration = p.GetFeatureDeclarationSection( key );
                if( declaration == null || IsFalse( declaration ) ) continue;
                var member = readers.FirstOrDefault( d => IsMember( setName, d.ComponentType ) );
                if( member == null )
                {
                    Error( declaration.Path, $"the feature '{readers[0].FeatureName}' ('{readers.Select( d => d.ComponentType.ToCSharpName() ).Concatenate( "', '" )}') is not a member of the type set of the party '{setName}'" );
                }
                else
                {
                    // The features of a remote are the ones of its local party (the party of the options).
                    CheckOptions( member.ComponentType, setName, name => p.TryGetFeatureSection( key, name ), partyDrivers[setName] );
                }
            }
        }

        // The options enabled in the section of an owner (true or a section) must be members of the set, and the party
        // features they require must be active in the party ("only with it": an explicit option without them is an error;
        // an option enabled by default is disabled with them at run time).
        void CheckOptions( Type owner, string setName, Func<string, ImmutableConfigurationSection?> find, IReadOnlySet<Type> activeDrivers )
        {
            foreach( var o in options.Where( o => o.Owner.IsAssignableFrom( owner ) ) )
            {
                if( find( o.Name ) is not { } s || IsFalse( s ) ) continue;
                if( !IsMember( setName, o.Type ) )
                {
                    Error( s.Path, $"the option '{o.Type:N}' is not a member of the type set of '{setName}'" );
                    continue;
                }
                foreach( var r in o.RequiredFeatures )
                {
                    if( !activeDrivers.Contains( r ) )
                    {
                        var required = result.Components.FirstOrDefault( d => d.ComponentType == r );
                        var name = required?.FeatureName ?? r.ToCSharpName();
                        monitor.Error( typeof( IApplicationIdentityFeatureDriver ).IsAssignableFrom( r )
                                        ? $"\"{Trim( s.Path )}\": the option '{o.Name}' requires the party feature '{name}' that is not active in the party '{setName}'."
                                        : $"\"{Trim( s.Path )}\": the option '{o.Name}' requires the client feature '{name}' that is not active in the client '{setName}'." );
                        success = false;
                    }
                }
            }
        }

        // The set of an instance of a template is the set of the template (see PartyComponentDirectory.FindTypeSetName).
        bool IsMember( string setName, Type t ) => PartyComponentDirectory.FindTypeSetName( sets.Keys, setName ) is not string n || sets[n].Contains( t );

        void Error( string path, string text )
        {
            monitor.Error( $"\"{Trim( path )}\": {text}. The types drive: a key doesn't bring its type in, add it to the \"Types\" of the set." );
            success = false;
        }

        // The paths are the ones of the projection ("CK-AppIdentity:Local:..."), without the root of the file.
        static string Trim( string path ) => path.StartsWith( "appsettings:", StringComparison.Ordinal ) ? path.Substring( 12 ) : path;
    }

    // Whether a component is active given its declaration: a Required one always, otherwise its declaration (true or a
    // section, not false) or, without declaration, its activation (ByDefault).
    static bool IsActive( PartyComponentInfo i, ImmutableConfigurationSection? declaration )
        => i.Activation == TypeActivation.Required
           || (declaration != null ? !IsFalse( declaration ) : i.Activation == TypeActivation.ByDefault);

    // The key of a feature driver in a party: its feature name, "XXXRemote" for an asymmetric feature on a remote.
    static string ConfigurationKey( PartyComponentInfo d, bool isLocal ) => !isLocal && d.Side != FeatureSide.None ? d.FeatureName + "Remote" : d.FeatureName;

    static bool IsFalse( ImmutableConfigurationSection s ) => string.Equals( s.Value, "false", StringComparison.OrdinalIgnoreCase );

    // Returns null on error (an invalid relation of an option).
    static List<Option>? CollectOptions( IActivityMonitor monitor, IRunningBinPathGroup group )
    {
        var options = new List<Option>();
        bool success = true;
        foreach( var cached in group.TypeSet )
        {
            var t = cached.Type;
            if( !t.IsClass || t.IsAbstract ) continue;
            var owner = t.GetInterfaces().FirstOrDefault( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IPartyComponentOption<> ) )?.GetGenericArguments()[0];
            if( owner == null ) continue;
            // The option directory reports the invalid declarations.
            if( cached.TryGetConfigurableType( monitor, out var configurable ) && configurable != null )
            {
                // The party features it requires: its non optional [Requires] to a feature driver (the reader of the
                // ResourceSpace: the rules of the topology).
                var visitor = new RequiredFeatureCollector();
                success &= ResPackageRelationReader.Read( monitor, cached, ref visitor );
                options.Add( new Option( configurable.Name, owner, t, (IReadOnlyList<Type>?)visitor.Features ?? [] ) );
            }
        }
        return success ? options : null;
    }

    struct RequiredFeatureCollector : IResPackageRelationVisitor
    {
        public List<Type>? Features;

        public void OnPackage( ResPackageDescriptor.Ref package ) { }

        public void OnRelation( ResRelationKind kind, ResPackageDescriptor.Ref target )
        {
            if( kind == ResRelationKind.Requires
                && !target.IsOptional
                && target.AsType is { } t
                && typeof( ApplicationIdentityFeatureDriver ).IsAssignableFrom( t ) )
            {
                (Features ??= []).Add( t );
            }
        }
    }
}
