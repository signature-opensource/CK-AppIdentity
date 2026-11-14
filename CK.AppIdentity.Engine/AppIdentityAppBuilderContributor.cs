using CK.AppIdentity;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

[assembly: CK.Setup.AppBuilderContributor( typeof( CK.Setup.AppIdentityAppBuilderContributor ) )]

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
            ApplicationIdentityServiceConfiguration? configuration;
            using( monitor.OpenInfo( $"Checking the party of {a}." ) )
            {
                // The relative paths (StoreRootPath) are the host's: its content root is its project folder.
                configuration = ApplicationIdentityServiceConfiguration.Create( monitor, section, defaultPartyName: a.Name, basePath: a.HostProjectPath );
                if( configuration == null )
                {
                    monitor.Error( $"Invalid party in {a}." );
                    success = false;
                    continue;
                }
            }
            // The type sets of the local parties.
            var root = CreateTypeSet( monitor, a, configuration.PartyName, configuration.LocalConfiguration.Configuration, null, ref success );
            foreach( var t in configuration.TenantDomains )
            {
                CreateTypeSet( monitor, a, t.PartyName, t.LocalConfiguration.Configuration, null, ref success );
            }
            var clients = section.TryGetSection( "Local" )?.TryGetSection( PartyClientSectionName );
            if( clients == null || root == null ) continue;
            foreach( var c in clients.GetChildren() )
            {
                var set = CreateTypeSet( monitor, a, c.Key, c, root, ref success );
                if( set != null ) _clients.Add( new DeclaredClient( a, c.Key, c, set ) );
            }
        }
        return success;

        // In the order of the configuration (the order of the declared clients is the one of the file).
        static void Copy( MutableConfigurationSection target, string path, ImmutableConfigurationSection s )
        {
            if( s.Value != null ) target[path] = s.Value;
            foreach( var c in s.GetChildren() ) Copy( target, $"{path}:{c.Key}", c );
        }
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
