using CK.Core;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// Creates the clients of a kind (a <see cref="PartyClient"/> type) for the parties that declare them. A package that
/// brings a kind of client implements one: it knows the clients it builds (<see cref="ClientNames"/>), and the
/// <see cref="PartyClientFeatureDriver"/> asks it to create the ones that a party declares in its "Local:PartyClient" section.
/// </summary>
[IsMultiple]
public interface IPartyClientFactory : ISingletonAutoService
{
    /// <summary>
    /// Gets the names of the clients that this factory builds. A name is built by one factory only.
    /// </summary>
    IReadOnlyCollection<string> ClientNames { get; }

    /// <summary>
    /// Gets the features that a client of this factory requires on its party: a party that declares one of its clients
    /// must allow them (<see cref="ApplicationIdentityFeatureDriver.IsAllowedFeature(IParty)"/>). The factory receives
    /// these drivers as constructor parameters.
    /// </summary>
    IReadOnlyList<ApplicationIdentityFeatureDriver> RequiredFeatures { get; }

    /// <summary>
    /// Creates a client that a party declares. The <paramref name="name"/> is one of the <see cref="ClientNames"/>, and
    /// the <see cref="RequiredFeatures"/> are allowed on the party. The factory validates the configuration: a fact
    /// that its kind of client doesn't have is an error.
    /// </summary>
    /// <param name="monitor">The monitor that receives the errors.</param>
    /// <param name="party">The local party that declares the client.</param>
    /// <param name="name">The client name.</param>
    /// <param name="configuration">The "Local:PartyClient:{Name}" configuration section.</param>
    /// <returns>The client, or null on error (the errors have been logged).</returns>
    PartyClient? Create( IActivityMonitor monitor, ILocalParty party, string name, ImmutableConfigurationSection configuration );
}
