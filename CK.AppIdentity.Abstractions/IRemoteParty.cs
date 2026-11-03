using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// A remote party can be an external one.
/// </summary>
public interface IRemoteParty : IOwnedParty
{
    /// <summary>
    /// Gets the <see cref="RemotePartyConfiguration"/> object.
    /// </summary>
    new RemotePartyConfiguration Configuration { get; }

    /// <summary>
    /// Gets whether this is an External party.
    /// </summary>
    bool IsExternalParty { get; }

    /// <summary>
    /// Gets the address of this party.
    /// This is null if this application cannot reach the remote (this remote must be a server that accepts the remote as a client).
    /// <para>
    /// This can also be null for external parties when the party is well known and features don't need a specific address (a GitHubApp
    /// feature for instance knows the default GitHub address).
    /// </para>
    /// </summary>
    string? Address { get; }

    /// <summary>
    /// Sets the <see cref="IParty.Description"/> of this remote, from its announcement. The description is persisted in
    /// this remote's folder of the <see cref="IParty.SharedFileStore"/> (it is the remote's own statement: the same for
    /// every local party), so that it is known while the remote is not connected, after a restart too. A change of the
    /// public URL is logged as a warning.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="description">The announced description.</param>
    /// <returns>True if the description changed, false if it is the same.</returns>
    bool SetDescription( IActivityMonitor monitor, PartyDescription description );

}
