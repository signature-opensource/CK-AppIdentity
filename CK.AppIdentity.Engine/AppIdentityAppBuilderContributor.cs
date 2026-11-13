using CK.AppIdentity;
using CK.Core;
using System.Collections.Generic;
using System.Linq;

[assembly: CK.Setup.AppBuilderContributor( typeof( CK.Setup.AppIdentityAppBuilderContributor ) )]

namespace CK.Setup;

/// <summary>
/// Reads the party of each App of the builder ("ckonf.json"): its keys (<see cref="ApplicationIdentityServiceConfiguration.RootKeys"/>:
/// "FullName", "Local", "Parties"...) are directly in the App. They are validated at build time
/// (<see cref="ApplicationIdentityServiceConfiguration"/>: an invalid configuration is a build error) and projected in the
/// "CK-AppIdentity" section of the host's "appsettings.json". The clients declared by its root party
/// ("Local:PartyClient:{Name}") are exposed to the contributors that handle their kind (their "Type": "TypeScript" for
/// instance). A declared client that no contributor handles is an error.
/// </summary>
public sealed class AppIdentityAppBuilderContributor : IAppBuilderContributor
{
    /// <summary>
    /// The section of the host's configuration that holds the party: "CK-AppIdentity".
    /// </summary>
    public const string SectionName = "CK-AppIdentity";

    readonly List<DeclaredClient> _clients = new();

    /// <summary>
    /// A client declared by the root party of an App.
    /// </summary>
    public sealed class DeclaredClient
    {
        internal DeclaredClient( AppConfiguration app, string name, string type, ImmutableConfigurationSection configuration )
        {
            App = app;
            Name = name;
            Type = type;
            Configuration = configuration;
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
        /// Gets the kind of client: its "Type" (a compile time key: it is not projected).
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the configuration of this client ("Local:PartyClient:{Name}" of the App).
        /// </summary>
        public ImmutableConfigurationSection Configuration { get; }

        /// <summary>
        /// Gets the path of this client's section relative to its <see cref="AppConfiguration.Configuration"/>: the prefix
        /// of the compile time keys of the client (see <see cref="AppConfiguration.AddCompileKey(string)"/>).
        /// </summary>
        public string RelativePath => $"Local:{PartyClientSectionName}:{Name}";

        /// <summary>
        /// Gets whether a contributor handled this client.
        /// </summary>
        public bool IsHandled { get; private set; }

        /// <summary>
        /// Called by the contributor that handles this kind of client.
        /// </summary>
        public void SetHandled() => IsHandled = true;

        /// <inheritdoc />
        public override string ToString() => $"client '{Name}' of {App}";
    }

    // The "PartyClient" feature's section (CK.AppIdentity.PartyClient is not referenced by this engine).
    const string PartyClientSectionName = "PartyClient";

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
            using( monitor.OpenInfo( $"Checking the party of {a}." ) )
            {
                // The relative paths (StoreRootPath) are the host's: its content root is its project folder.
                if( ApplicationIdentityServiceConfiguration.Create( monitor, section, defaultPartyName: a.Name, basePath: a.HostProjectPath ) == null )
                {
                    monitor.Error( $"Invalid party in {a}." );
                    success = false;
                    continue;
                }
            }
            var clients = section.TryGetSection( "Local" )?.TryGetSection( PartyClientSectionName );
            if( clients == null ) continue;
            foreach( var c in clients.GetChildren() )
            {
                var type = c["Type"];
                if( string.IsNullOrWhiteSpace( type ) )
                {
                    monitor.Error( $"The client '{c.Key}' of {a} has no \"Type\": the kind of a client is explicit (\"Type\": \"TypeScript\" for instance)." );
                    success = false;
                    continue;
                }
                var client = new DeclaredClient( a, c.Key, type, c );
                a.AddCompileKey( $"{client.RelativePath}:Type" );
                _clients.Add( client );
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

    /// <inheritdoc />
    public bool OnContributed( IActivityMonitor monitor, AppBuilderContext context )
    {
        bool success = true;
        foreach( var c in _clients.Where( c => !c.IsHandled ) )
        {
            monitor.Error( $"No package builds the {c} of Type '{c.Type}' (a missing package reference in the builder?)." );
            success = false;
        }
        return success;
    }
}
