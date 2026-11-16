using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// A feature of a <see cref="PartyClient"/>: a client is a bag of client features, as a party is a bag of features. A
/// package that brings a client feature specializes this class and its <see cref="ClientFeatureDriver{T}"/>.
/// <para>
/// Its name is declared by its <see cref="PartyClientFeatureAttribute"/> (<c>[PartyClientFeature( "NgApp", TypeActivation.Optional )]</c>):
/// it is its key in the "Local:PartyClient:{Name}" section of its client. Its requirements are declared with <c>[Requires&lt;T&gt;]</c>: a
/// feature driver of the party (<c>[Requires&lt;HttpEndpointFeatureDriver&gt;]</c>), an abstraction implemented by another
/// feature of the client (<c>[Requires&lt;IWebAppBuild&gt;]</c>, see <see cref="IPartyComponentAbstraction"/>).
/// </para>
/// <para>
/// A client feature is a party component: it owns options (<see cref="PartyClientFeatureOptionAttribute"/>).
/// </para>
/// </summary>
public abstract class ClientFeature : IPartyComponent
{
    /// <summary>
    /// Initializes a new client feature.
    /// </summary>
    /// <param name="client">The client of this feature.</param>
    /// <param name="configuration">Its "Local:PartyClient:{Name}:{FeatureName}" configuration section.</param>
    protected ClientFeature( PartyClient client, ImmutableConfigurationSection configuration )
    {
        Throw.CheckNotNullArgument( client );
        Throw.CheckNotNullArgument( configuration );
        Client = client;
        Configuration = configuration;
    }

    // Set by the PartyClientFeatureDriver once created (from the PartyComponentDirectory).
    internal string? _name;

    /// <summary>
    /// Gets the client of this feature.
    /// </summary>
    public PartyClient Client { get; }

    /// <summary>
    /// Gets the name of this feature: the one declared by its <see cref="PartyClientFeatureAttribute"/> (the engine reads it, see
    /// <see cref="PartyComponentInfo.FeatureName"/>). Available once the feature is created.
    /// </summary>
    public string Name => _name ?? Throw.InvalidOperationException<string>( $"The name of the client feature '{GetType().Name}' is set once it is created." );

    /// <summary>
    /// Gets the "Local:PartyClient:{Name}:{FeatureName}" configuration section of this feature.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <summary>
    /// Gets the options of this feature, in their order: the ones of its type (or of a base of it, see
    /// <see cref="PartyClientFeatureOptionAttribute"/>) enabled by its section. They are created once, with the feature: an option
    /// reads its configuration in its constructor (an invalid one makes the client an error). Applying an option is the
    /// feature's business.
    /// </summary>
    public IReadOnlyList<object> Options { get; internal set; } = [];

    /// <summary>
    /// Gets an option of this feature (see <see cref="Options"/>).
    /// </summary>
    /// <typeparam name="T">The option type.</typeparam>
    /// <returns>The option, null when it is not enabled.</returns>
    public T? GetOption<T>() where T : class => Options.OfType<T>().FirstOrDefault();

    /// <summary>
    /// Contributes to the description of the client (<see cref="PartyClient.Description"/>): its request path (when the
    /// party serves it), its facts. Does nothing by default.
    /// </summary>
    /// <param name="builder">The description of the client.</param>
    protected internal virtual void ContributeDescription( ClientDescriptionBuilder builder )
    {
    }

    /// <inheritdoc />
    public override string ToString() => $"{_name ?? GetType().Name} of {Client}";
}
