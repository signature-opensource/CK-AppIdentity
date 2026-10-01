using CK.Core;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// A owned party belongs to the root <see cref="IApplicationIdentityService"/> or to a <see cref="ITenantDomainParty"/>.
/// It can be a <see cref="IRemoteParty"/> or a <see cref="ITenantDomainParty"/>.
/// <para>
/// A owned party can be destroyed if it has been dynamically created.
/// </para>
/// </summary>
public interface IOwnedParty : IParty
{
    /// <summary>
    /// Gets this party's owner.
    /// This can be the root <see cref="IApplicationIdentityService"/> or a <see cref="ITenantDomainParty"/>.
    /// </summary>
    ILocalParty Owner { get; }

    /// <summary>
    /// Gets whether this is a dynamic remote.
    /// </summary>
    bool IsDynamic { get; }

    /// <summary>
    /// Gets whether this remote has been destroyed.
    /// </summary>
    bool IsDestroyed { get; }

    /// <summary>
    /// Initiates the destruction of this remote. <see cref="IsDynamic"/> must be true
    /// otherwise an <see cref="System.InvalidOperationException"/> is thrown.
    /// </summary>
    /// <returns>True if this call destroyed this remote, false it is already destroyed.</returns>
    bool SetDestroyed();

    /// <summary>
    /// Destroys this remote. Even if <see cref="SetDestroyed"/> has been called, awaiting this
    /// waits for this remote to be actually destroyed (this can always be awaited).
    /// <para>
    /// <see cref="IsDynamic"/> must be true otherwise an <see cref="System.InvalidOperationException"/> is thrown.
    /// </para>
    /// <para>
    /// This task always completes: when the service is stopped, the party has been shut down with it and the
    /// task completes immediately.
    /// </para>
    /// <para>
    /// The party's <see cref="IParty.SharedFileStore"/> folder is kept: it is shared by the applications that use the same
    /// store (the party may still be alive in them) and it holds the party's pinned identity, that must outlive the destruction
    /// (like a "known_hosts" entry): adding the party again finds it. Only its trash bin is purged
    /// (see <see cref="ApplicationIdentityServiceConfiguration.TrashBinRetention"/>).
    /// </para>
    /// </summary>
    /// <returns>The awaitable.</returns>
    Task DestroyAsync();

}
