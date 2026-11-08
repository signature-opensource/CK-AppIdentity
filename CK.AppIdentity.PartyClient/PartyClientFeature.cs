using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The "PartyClient" feature of a local party: its clients (see <see cref="PartyClientFeatureDriver"/>).
/// </summary>
public sealed class PartyClientFeature
{
    internal PartyClientFeature( ILocalParty party, IReadOnlyList<PartyClient> clients )
    {
        Party = party;
        Clients = clients;
    }

    /// <summary>
    /// Gets the local party.
    /// </summary>
    public ILocalParty Party { get; }

    /// <summary>
    /// Gets the clients of the party, in the order of their declaration.
    /// </summary>
    public IReadOnlyList<PartyClient> Clients { get; }

    /// <summary>
    /// Finds a client by its name.
    /// </summary>
    /// <param name="name">The client name.</param>
    /// <returns>The client or null.</returns>
    public PartyClient? Find( string name ) => Clients.FirstOrDefault( c => c.Name == name );
}
