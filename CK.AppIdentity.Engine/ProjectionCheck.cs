using CK.AppIdentity;
using CK.Core;
using CK.Engine.TypeCollector;
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
    static readonly HashSet<string> _typeSetKeys = new( StringComparer.OrdinalIgnoreCase ) { "Types", "ExcludedTypes" };

    sealed record Option( string Name, Type Owner, Type Type );

    public static bool Run( IActivityMonitor monitor, IRunningBinPathGroup group, PartyComponentAnalysis.Result result )
    {
        if( group.TypeSets.Count == 0 ) return true;
        bool success = true;
        foreach( var b in group.SimilarConfigurations )
        {
            if( b.ProjectPath.IsEmptyPath ) continue;
            var file = b.ProjectPath.AppendPart( "appsettings.json" );
            if( !File.Exists( file ) ) continue;
            var section = ImmutableConfigurationSection.CreateFromJson( "appsettings", File.ReadAllText( file ) ).TryGetSection( AppIdentityAppBuilderContributor.SectionName );
            if( section == null ) continue;
            using( monitor.OpenTrace( $"Checking the type sets of the party projected in '{file}'." ) )
            {
                var c = ApplicationIdentityServiceConfiguration.Create( monitor, section );
                if( c == null ) return false;
                success &= Check( monitor, group, result, c );
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
        var partyDrivers = new Dictionary<string, HashSet<Type>>( StringComparer.OrdinalIgnoreCase );
        bool success = true;

        CheckParty( c.PartyName, c.LocalConfiguration, c.Remotes );
        foreach( var t in c.TenantDomains ) CheckParty( t.PartyName, t.LocalConfiguration, t.Remotes );
        // The clients are declared by the root party only.
        if( c.LocalConfiguration.Configuration.TryGetSection( PartyClientSectionName ) is { } clients )
        {
            var outer = partyDrivers[c.PartyName];
            foreach( var client in clients.GetChildren() )
            {
                var scope = clientFeatures.Where( f => IsMember( client.Key, f.ComponentType ) && IsActive( f, client.TryGetSection( f.FeatureName ) ) ).ToList();
                success &= PartyComponentDirectory.CheckScope( monitor, $"the client '{client.Key}'", scope, result.MultipleAbstractions.Contains, outer.Contains );
                foreach( var k in client.GetChildren() )
                {
                    if( _typeSetKeys.Contains( k.Key ) || IsFalse( k ) ) continue;
                    var f = clientFeatures.FirstOrDefault( i => string.Equals( i.FeatureName, k.Key, StringComparison.OrdinalIgnoreCase ) );
                    if( f == null ) continue;
                    if( !IsMember( client.Key, f.ComponentType ) )
                    {
                        Error( k.Path, $"the client feature '{f.ComponentType:N}' is not a member of the type set of the client '{client.Key}'" );
                    }
                    else if( k.Value == null )
                    {
                        CheckOptions( f.ComponentType, client.Key, name => k.TryGetSection( name ) );
                    }
                }
            }
        }
        return success;

        void CheckParty( string partyName, ApplicationIdentityConfiguration local, IEnumerable<ApplicationIdentityConfiguration> remotes )
        {
            CheckKeys( partyName, local, isLocal: true );
            foreach( var r in remotes ) CheckKeys( partyName, r, isLocal: false );
            var scope = drivers.Where( d => IsMember( partyName, d.ComponentType ) && IsActive( d, local.GetFeatureDeclarationSection( d.FeatureName ) ) ).ToList();
            partyDrivers[partyName] = scope.Select( d => d.ComponentType ).ToHashSet();
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
                    CheckOptions( member.ComponentType, setName, name => p.TryGetFeatureSection( key, name ) );
                }
            }
        }

        // The options enabled in the section of an owner (true or a section) must be members of the set.
        void CheckOptions( Type owner, string setName, Func<string, ImmutableConfigurationSection?> find )
        {
            foreach( var o in options.Where( o => o.Owner.IsAssignableFrom( owner ) ) )
            {
                if( find( o.Name ) is { } s && !IsFalse( s ) && !IsMember( setName, o.Type ) )
                {
                    Error( s.Path, $"the option '{o.Type:N}' is not a member of the type set of '{setName}'" );
                }
            }
        }

        bool IsMember( string setName, Type t ) => !sets.TryGetValue( setName, out var set ) || set.Contains( t );

        void Error( string path, string text )
        {
            // The paths are the ones of the projection ("CK-AppIdentity:Local:..."), without the root of the file.
            if( path.StartsWith( "appsettings:", StringComparison.Ordinal ) ) path = path.Substring( 12 );
            monitor.Error( $"\"{path}\": {text}. The types drive: a key doesn't bring its type in, add it to the \"Types\" of the set." );
            success = false;
        }
    }

    // Whether a component is active given its declaration: a Required one always, otherwise its declaration (true or a
    // section, not false) or, without declaration, its activation (ByDefault).
    static bool IsActive( PartyComponentInfo i, ImmutableConfigurationSection? declaration )
        => i.Activation == TypeActivation.Required
           || (declaration != null ? !IsFalse( declaration ) : i.Activation == TypeActivation.ByDefault);

    // The key of a feature driver in a party: its feature name, "XXXRemote" for an asymmetric feature on a remote.
    static string ConfigurationKey( PartyComponentInfo d, bool isLocal ) => !isLocal && d.Side != FeatureSide.None ? d.FeatureName + "Remote" : d.FeatureName;

    static bool IsFalse( ImmutableConfigurationSection s ) => string.Equals( s.Value, "false", StringComparison.OrdinalIgnoreCase );

    static List<Option> CollectOptions( IActivityMonitor monitor, IRunningBinPathGroup group )
    {
        var options = new List<Option>();
        foreach( var cached in group.TypeSet )
        {
            var t = cached.Type;
            if( !t.IsClass || t.IsAbstract ) continue;
            var owner = t.GetInterfaces().FirstOrDefault( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IPartyComponentOption<> ) )?.GetGenericArguments()[0];
            if( owner == null ) continue;
            // The option directory reports the invalid declarations.
            if( cached.TryGetConfigurableType( monitor, out var configurable ) && configurable != null )
            {
                options.Add( new Option( configurable.Name, owner, t ) );
            }
        }
        return options;
    }
}
