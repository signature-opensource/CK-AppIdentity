using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// A client of a local party: an application that sends commands to the party on behalf of users (a browser application
/// served by the party, a local tool, a native application...). A client is not a party: it has no identity key and it
/// is not a remote.
/// <para>
/// Its type is its kind: a package that brings a kind of client specializes this class and creates its instances
/// (see <see cref="IPartyClientFactory"/>). A client is configured in the "Local:PartyClient:{Name}" section of its party.
/// </para>
/// </summary>
public abstract class PartyClient : IPartyComponent
{
    /// <summary>
    /// Initializes a new client.
    /// </summary>
    /// <param name="party">The local party of this client.</param>
    /// <param name="name">The client name (unique in its party).</param>
    /// <param name="configuration">Its "Local:PartyClient:{Name}" configuration section.</param>
    protected PartyClient( ILocalParty party, string name, ImmutableConfigurationSection configuration )
    {
        Throw.CheckNotNullArgument( party );
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckNotNullArgument( configuration );
        Party = party;
        Name = name;
        Configuration = configuration;
    }

    /// <summary>
    /// Gets the local party of this client.
    /// </summary>
    public ILocalParty Party { get; }

    /// <summary>
    /// Gets the name of this client: unique in its <see cref="Party"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the "Local:PartyClient:{Name}" configuration section of this client.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <summary>
    /// Gets what this client states to the remotes, in the description of its party (<see cref="PartyDescription.ClientApplications"/>).
    /// Null when the client is not announced (a local tool).
    /// <para>
    /// Defaults to null.
    /// </para>
    /// </summary>
    public virtual ClientApplicationDescription? Description => null;

    /// <summary>
    /// Gets the options of this client, in their order: the ones of its type (or of a base of it, see
    /// <see cref="PartyClientOptionAttribute"/>) enabled by its "Local:PartyClient:{Name}" section. They are set once, when
    /// the client is created (see <see cref="InitializeOptions"/>).
    /// </summary>
    public IReadOnlyList<object> Options { get; private set; } = [];

    /// <summary>
    /// Called once, when the client is created, with its selected options (in their order) and their configuration
    /// section (null for an option enabled by <c>true</c> or by default). Applying an option is the client's business: a
    /// kind of client that has options checks here that they are the ones it expects (a base type of its own, for
    /// instance) and initializes them.
    /// <para>
    /// Accepts the options by default. Returning false (an error must be logged) makes the client an error.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="options">The selected options and their configuration.</param>
    /// <returns>True on success, false on error.</returns>
    protected virtual bool InitializeOptions( IActivityMonitor monitor, IReadOnlyList<(object Option, ImmutableConfigurationSection? Configuration)> options ) => true;

    internal bool SetOptions( IActivityMonitor monitor, IReadOnlyList<(object Option, ImmutableConfigurationSection? Configuration)> options )
    {
        if( !InitializeOptions( monitor, options ) ) return false;
        Options = options.Select( o => o.Option ).ToArray();
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name} '{Name}' of '{Party.FullName}'";
}
