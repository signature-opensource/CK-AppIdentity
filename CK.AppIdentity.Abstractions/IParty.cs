using CK.Core;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// All exposed entities of the application identity model are parties.
/// </summary>
public interface IParty
{
    /// <summary>
    /// Gets the configuration object.
    /// <para>
    /// It can be:
    /// <list type="bullet">
    ///  <item>A <see cref="RemotePartyConfiguration"/>.</item>
    ///  <item>A <see cref="ApplicationIdentityServiceConfiguration"/>.</item>
    ///  <item>A <see cref="TenantDomainPartyConfiguration"/>.</item>
    /// </list>
    /// The <see cref="ApplicationIdentityPartyConfiguration.Configuration"/> immutable configuration section contain any
    /// possible feature configuration regardless of this object's type or the configuration type.
    /// </para>
    /// </summary>
    ApplicationIdentityPartyConfiguration Configuration { get; }

    /// <summary>
    /// Gets the root application identity service.
    /// </summary>
    IApplicationIdentityService ApplicationIdentityService { get; }

    /// <summary>
    /// Gets the domain name.
    /// </summary>
    string DomainName { get; }

    /// <summary>
    /// Gets the environment name.
    /// </summary>
    string EnvironmentName { get; }

    /// <summary>
    /// Gets the party name.
    /// </summary>
    string PartyName { get; }

    /// <summary>
    /// Gets the full name of this party.
    /// <para>
    /// The full name identifies the party: it is unique regardless of the case (<see cref="System.StringComparer.OrdinalIgnoreCase"/>).
    /// Two parties whose full names differ only by case cannot coexist (this is stricter than
    /// <see cref="CoreApplicationIdentity"/> that only states that the <see cref="CoreApplicationIdentity.DomainName"/> is case sensitive):
    /// this notably avoids <see cref="SharedFileStore"/> clashes on case insensitive file systems.
    /// </para>
    /// <para>
    /// Thanks to this, comparing full names with the <see cref="NormalizedPath"/> equality (that uses <see cref="System.StringComparer.Ordinal"/>)
    /// is safe.
    /// </para>
    /// </summary>
    NormalizedPath FullName { get; }

    /// <summary>
    /// Gets the actor identifier of this party in the actors model (its PartyActor), or 0 until it is resolved.
    /// <para>
    /// It is the identity of the party when it calls another one or receives a command directly: the authentication
    /// information of a received command carries it as its caller party. It is resolved by an initialization step
    /// (the PartyActor registrar of the authentication center packages) and set once with <see cref="TrySetPartyActorId"/>.
    /// A party whose identifier is 0 cannot be a caller party.
    /// </para>
    /// </summary>
    int PartyActorId { get; }

    /// <summary>
    /// Sets the <see cref="PartyActorId"/> once: it can only go from 0 to an identifier, it never changes afterwards.
    /// This is a security-relevant value: the change is logged, a different identifier is refused and logged.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="partyActorId">The actor identifier of this party. Must be positive.</param>
    /// <returns>True if the identifier is set (or was already this one), false if another identifier is already set.</returns>
    bool TrySetPartyActorId( IActivityMonitor monitor, int partyActorId );

    /// <summary>
    /// Gets the features associated to this <see cref="ApplicationIdentityService"/>, <see cref="IRemoteParty"/> or <see cref="ILocalParty"/>.
    /// </summary>
    IEnumerable<object> Features { get; }

    /// <summary>
    /// Atomically (thread safe) adds a feature if it doesn't already exist.
    /// </summary>
    /// <param name="feature">The feature to add.</param>
    void AddFeature( object feature );

    /// <summary>
    /// Gets the first object feature that is a <typeparamref name="T"/> or null if not found.
    /// </summary>
    /// <typeparam name="T">The feature type.</typeparam>
    /// <returns>The feature or null.</returns>
    T? GetFeature<T>();

    /// <summary>
    /// Gets the first object feature that is a <typeparamref name="T"/> or throws
    /// an <see cref="System.InvalidOperationException"/>
    /// </summary>
    /// <typeparam name="T">The feature type.</typeparam>
    /// <returns>The feature.</returns>
    T GetRequiredFeature<T>();

    /// <summary>
    /// Gets shared file store for this party.
    /// <para>
    /// This directory is shared by all local parties that runs on this computer/file system.
    /// Use <see cref="ILocalParty.LocalFileStore"/> to obtain the "-Local" directory of a local party.
    /// </para>
    /// </summary>
    IFileStore SharedFileStore { get; }

    /// <summary>
    /// Overridden to return the <see cref="FullName"/>.
    /// </summary>
    /// <returns>This full name.</returns>
    string ToString();
}
