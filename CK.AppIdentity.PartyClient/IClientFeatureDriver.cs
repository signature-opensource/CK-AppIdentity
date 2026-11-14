using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// Creates the client features of a kind (a <see cref="ClientFeature"/> type) for the clients that declare them. A
/// package that brings a client feature implements one, by specializing <see cref="ClientFeatureDriver{T}"/>.
/// </summary>
[IsMultiple]
public interface IClientFeatureDriver : ISingletonAutoService
{
    /// <summary>
    /// Gets the type of the features that this driver creates.
    /// </summary>
    Type FeatureType { get; }

    /// <summary>
    /// Creates the feature of a client that declares it. Its requirements are checked: the driver validates its
    /// configuration.
    /// </summary>
    /// <param name="monitor">The monitor that receives the errors.</param>
    /// <param name="client">The client.</param>
    /// <param name="configuration">The "Local:PartyClient:{Name}:{FeatureName}" configuration section.</param>
    /// <returns>The feature, or null on error (the errors have been logged).</returns>
    ClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration );
}

/// <summary>
/// The base of the drivers of the client features of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the client features.</typeparam>
public abstract class ClientFeatureDriver<T> : IClientFeatureDriver where T : ClientFeature
{
    /// <inheritdoc />
    public Type FeatureType => typeof( T );

    /// <inheritdoc cref="IClientFeatureDriver.Create" />
    protected abstract T? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration );

    ClientFeature? IClientFeatureDriver.Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
    {
        return Create( monitor, client, configuration );
    }
}
