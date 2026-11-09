using CK.Core;
using CK.PerfectEvent;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// A local party is the root <see cref="IApplicationIdentityService"/> or a <see cref="ITenantDomainParty"/>.
/// It owns remotes (the root service also owns tenant domains).
/// </summary>
public interface ILocalParty : IParty
{
    /// <summary>
    /// Raised whenever a new party appears or disappears in this <see cref="Remotes"/>.
    /// <para>
    /// By subscribing to this event on the root <see cref="IApplicationIdentityService"/>, one can track any structural
    /// change of the whole identity system except new or destroyed <see cref="ITenantDomainParty"/>: use
    /// <see cref="IApplicationIdentityService.AnyPartyChanged"/> to track tenant domains.
    /// </para>
    /// </summary>
    PerfectEvent<IRemoteParty> AnyRemoteChanged { get; }

    /// <summary>
    /// Gets the remote parties.
    /// <para>
    /// This is a snapshot: while enumerating <see cref="IOwnedParty.IsDestroyed"/> may be true (or becomes true at any time).
    /// </para>
    /// </summary>
    IReadOnlyCollection<IRemoteParty> Remotes { get; }

    /// <summary>
    /// Tries to create and initialize one or more new remotes from a configuration section.
    /// These remotes will be <see cref="IOwnedParty.IsDynamic"/> and can be destroyed.
    /// <para>
    /// No tenant domains must appear in the configuration otherwise an <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// <para>
    /// This is all or nothing: if any remote fails to be set up, none of them is added.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The configuration to process.</param>
    /// <returns>The newly created remotes or null if an error occurred.</returns>
    Task<IReadOnlyCollection<IRemoteParty>?> AddMultipleRemotesAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration );

    /// <summary>
    /// Tries to create and initialize a single remote from a configuration section.
    /// This remote will be <see cref="IOwnedParty.IsDynamic"/> and can be destroyed.
    /// <para>
    /// Only one remote must appear in the configuration otherwise an <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The configuration to process.</param>
    /// <returns>The newly created remote or null if an error occurred.</returns>
    Task<IRemoteParty?> AddRemoteAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration );

    /// <summary>
    /// Raised when the <see cref="IParty.Description"/> of this local party changes (the HTTP endpoints, for instance,
    /// know their URL once the host has started): the channels announce it again to the remotes.
    /// </summary>
    PerfectEvent<ILocalParty> DescriptionChanged { get; }

    /// <summary>
    /// Raised when the <see cref="IParty.Description"/> of one of the <see cref="Remotes"/> changes: the remote announced
    /// another description (the last known description read when the remote is set up raises nothing). It is raised by
    /// the loop of the application identity agent, one change at a time, in their order.
    /// </summary>
    PerfectEvent<IRemoteParty> RemoteDescriptionChanged { get; }

    /// <summary>
    /// Gets the "Local" configuration section.
    /// </summary>
    ApplicationIdentityLocalConfiguration LocalConfiguration { get; }

    /// <summary>
    /// Gets the private local file store. This is the "-Local" directory inside this <see cref="IParty.SharedFileStore"/>.
    /// <para>
    /// It is private to this party: the <see cref="IParty.SharedFileStore"/> can't access it. It is not private to the
    /// account: in a shared store, it has the same permissions as the rest of the store
    /// (see <see cref="ApplicationIdentityServiceConfiguration.StoreRootPath"/>).
    /// </para>
    /// </summary>
    IFileStore LocalFileStore { get; }

}
