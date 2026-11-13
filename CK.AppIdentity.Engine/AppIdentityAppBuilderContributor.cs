using CK.AppIdentity;
using CK.Core;
using System.Collections.Generic;
using System.Linq;

[assembly: CK.Setup.AppBuilderContributor( typeof( CK.Setup.AppIdentityAppBuilderContributor ) )]

namespace CK.Setup;

/// <summary>
/// Reads the party of each BinPath of the App ("ckonf.json"): its "CK-AppIdentity" section is validated at build time
/// (<see cref="ApplicationIdentityServiceConfiguration"/>: an invalid configuration is a build error), and the clients
/// declared by its root party ("CK-AppIdentity:Local:PartyClient:{Name}") are exposed to the contributors that handle
/// their kind (their "Type": "TypeScript" for instance). A declared client that no contributor handles is an error.
/// </summary>
public sealed class AppIdentityAppBuilderContributor : IAppBuilderContributor
{
    readonly List<DeclaredClient> _clients = new();

    /// <summary>
    /// A client declared by the root party of a BinPath.
    /// </summary>
    public sealed class DeclaredClient
    {
        internal DeclaredClient( AppBinPath binPath, string name, string type, ImmutableConfigurationSection configuration )
        {
            BinPath = binPath;
            Name = name;
            Type = type;
            Configuration = configuration;
        }

        /// <summary>
        /// Gets the BinPath (the party) that declares this client.
        /// </summary>
        public AppBinPath BinPath { get; }

        /// <summary>
        /// Gets the client name: its key in "CK-AppIdentity:Local:PartyClient".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the kind of client: its "Type" (a compile time key: it is not projected).
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the configuration of this client ("CK-AppIdentity:Local:PartyClient:{Name}").
        /// </summary>
        public ImmutableConfigurationSection Configuration { get; }

        /// <summary>
        /// Gets the path of this client's section relative to its <see cref="AppBinPath.Configuration"/>: the prefix
        /// of the compile time keys of the client (see <see cref="AppBinPath.AddCompileKey(string)"/>).
        /// </summary>
        public string RelativePath => $"CK-AppIdentity:Local:{PartyClientSectionName}:{Name}";

        /// <summary>
        /// Gets whether a contributor handled this client.
        /// </summary>
        public bool IsHandled { get; private set; }

        /// <summary>
        /// Called by the contributor that handles this kind of client.
        /// </summary>
        public void SetHandled() => IsHandled = true;

        /// <inheritdoc />
        public override string ToString() => $"client '{Name}' of {BinPath}";
    }

    // The "PartyClient" feature's section (CK.AppIdentity.PartyClient is not referenced by this engine).
    const string PartyClientSectionName = "PartyClient";

    /// <summary>
    /// Gets the clients declared by the root party of each BinPath.
    /// </summary>
    public IReadOnlyList<DeclaredClient> Clients => _clients;

    /// <inheritdoc />
    public bool Contribute( IActivityMonitor monitor, AppBuilderContext context )
    {
        bool success = true;
        foreach( var b in context.BinPaths )
        {
            var section = b.Configuration.TryGetSection( "CK-AppIdentity" );
            if( section == null ) continue;
            using( monitor.OpenInfo( $"Checking the party of {b}." ) )
            {
                // The relative paths (StoreRootPath) are the host's: its content root is its project folder.
                if( ApplicationIdentityServiceConfiguration.Create( monitor, section, defaultPartyName: b.Name, basePath: b.HostProjectPath ) == null )
                {
                    monitor.Error( $"Invalid \"CK-AppIdentity\" in {b}." );
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
                    monitor.Error( $"The client '{c.Key}' of {b} has no \"Type\": the kind of a client is explicit (\"Type\": \"TypeScript\" for instance)." );
                    success = false;
                    continue;
                }
                var client = new DeclaredClient( b, c.Key, type, c );
                b.AddCompileKey( $"{client.RelativePath}:Type" );
                _clients.Add( client );
            }
        }
        return success;
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
