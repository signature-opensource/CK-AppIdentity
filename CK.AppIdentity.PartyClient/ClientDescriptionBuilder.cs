namespace CK.AppIdentity;

/// <summary>
/// The description of a client being assembled from its features (see <see cref="ClientFeature.ContributeDescription"/>).
/// </summary>
public sealed class ClientDescriptionBuilder
{
    ClientApplicationFacts _facts = ClientApplicationFacts.Empty;

    internal ClientDescriptionBuilder( PartyClient client ) => Client = client;

    /// <summary>
    /// Gets the client being described.
    /// </summary>
    public PartyClient Client { get; }

    /// <summary>
    /// Gets or sets the request path under which the party serves the client ("/" for its default one). A client that has
    /// no request path is not described (it is not served by the party).
    /// </summary>
    public string? RequestPath { get; set; }

    /// <summary>
    /// Gets the facts of the client.
    /// </summary>
    public ClientApplicationFacts Facts => _facts;

    /// <summary>
    /// Adds facts.
    /// </summary>
    /// <param name="facts">The facts to add.</param>
    public void AddFacts( ClientApplicationFacts facts ) => _facts = _facts.Union( facts );
}
