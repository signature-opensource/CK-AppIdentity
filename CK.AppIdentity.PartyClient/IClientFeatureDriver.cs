using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// Creates the client features of a kind (a <see cref="ClientFeature"/> type) for the clients that declare them. A
/// package that brings a client feature implements one, by specializing <see cref="ClientFeatureDriver{T}"/>, or
/// <see cref="ClientFeatureDriver{T, TModel}"/> when the feature has a configuration model (its <c>[Configuration&lt;TModel&gt;]</c>).
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

    /// <summary>
    /// Creates the feature of a client that declares it, with its configuration model: the PartyClient feature reads it
    /// from the feature's section (its options' names reserved) when the feature has one. A driver without model ignores
    /// it.
    /// </summary>
    /// <param name="monitor">The monitor that receives the errors.</param>
    /// <param name="client">The client.</param>
    /// <param name="configuration">The "Local:PartyClient:{Name}:{FeatureName}" configuration section.</param>
    /// <param name="model">The configuration model of the feature, null when it has none.</param>
    /// <returns>The feature, or null on error (the errors have been logged).</returns>
    ClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration, object? model )
        => Create( monitor, client, configuration );
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

/// <summary>
/// The base of the drivers of the client features of type <typeparamref name="T"/> that have a configuration model: the
/// feature type is decorated with <c>[Configuration&lt;TModel&gt;]</c>, the driver receives the model read from its
/// section (an unknown key, a missing required one, an invalid value are errors before it is called).
/// </summary>
/// <typeparam name="T">The type of the client features.</typeparam>
/// <typeparam name="TModel">The configuration model of the features.</typeparam>
public abstract class ClientFeatureDriver<T, TModel> : IClientFeatureDriver where T : ClientFeature where TModel : class
{
    /// <inheritdoc />
    public Type FeatureType => typeof( T );

    /// <summary>
    /// Creates the feature of a client that declares it, from its model. Its requirements are checked.
    /// </summary>
    /// <param name="monitor">The monitor that receives the errors.</param>
    /// <param name="client">The client.</param>
    /// <param name="configuration">The "Local:PartyClient:{Name}:{FeatureName}" configuration section.</param>
    /// <param name="model">The configuration model.</param>
    /// <returns>The feature, or null on error (the errors have been logged).</returns>
    protected abstract T? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration, TModel model );

    ClientFeature? IClientFeatureDriver.Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
    {
        monitor.Error( $"The client feature '{typeof( T ).ToCSharpName()}' has a configuration model ('{typeof( TModel ).ToCSharpName()}'): it is created with it (its type must be decorated with [Configuration<{typeof( TModel ).Name}>])." );
        return null;
    }

    ClientFeature? IClientFeatureDriver.Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration, object? model )
    {
        if( model is not TModel m ) return ((IClientFeatureDriver)this).Create( monitor, client, configuration );
        return Create( monitor, client, configuration, m );
    }
}
