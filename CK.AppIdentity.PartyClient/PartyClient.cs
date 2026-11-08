using CK.Core;

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

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name} '{Name}' of '{Party.FullName}'";
}
