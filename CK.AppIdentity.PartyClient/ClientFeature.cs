using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// A feature of a <see cref="PartyClient"/>: a client is a bag of client features, as a party is a bag of features. A
/// package that brings a client feature specializes this class and its <see cref="ClientFeatureDriver{T}"/>.
/// <para>
/// Its name is its type name without the "ClientFeature" suffix (<c>NgAppClientFeature</c> is "NgApp"): it is its key in
/// the "Local:PartyClient:{Name}" section of its client. Its requirements are declared with <c>[Requires&lt;T&gt;]</c>: a
/// feature driver of the party (<c>[Requires&lt;HttpEndpointFeatureDriver&gt;]</c>), an abstraction implemented by another
/// feature of the client (<c>[Requires&lt;IWebAppBuild&gt;]</c>, see <see cref="IPartyComponentAbstraction"/>).
/// </para>
/// <para>
/// A client feature is a party component: it owns options (<see cref="PartyClientOptionAttribute"/>).
/// </para>
/// </summary>
[ClientFeatureName]
public abstract class ClientFeature : IPartyComponent
{
    /// <summary>
    /// The suffix of the type name of a client feature: its name is its type name without it.
    /// </summary>
    public const string TypeNameSuffix = "ClientFeature";

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
    /// Gets the name of this feature: its type name without the "ClientFeature" suffix (the engine computes it, see
    /// <see cref="PartyComponentInfo.FeatureName"/>). Available once the feature is created.
    /// </summary>
    public string Name => _name ?? Throw.InvalidOperationException<string>( $"The name of the client feature '{GetType().Name}' is set once it is created." );

    /// <summary>
    /// Gets the "Local:PartyClient:{Name}:{FeatureName}" configuration section of this feature.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <summary>
    /// Gets the options of this feature, in their order: the ones of its type (or of a base of it, see
    /// <see cref="PartyClientOptionAttribute"/>) enabled by its section. They are set once, when the feature is created
    /// (see <see cref="InitializeOptions"/>).
    /// </summary>
    public IReadOnlyList<object> Options { get; private set; } = [];

    /// <summary>
    /// Called once, when the feature is created, with its selected options (in their order) and their configuration
    /// section (null for an option enabled by <c>true</c> or by default). Applying an option is the feature's business.
    /// <para>
    /// Accepts the options by default. Returning false (an error must be logged) makes the client an error.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="options">The selected options and their configuration.</param>
    /// <returns>True on success, false on error.</returns>
    protected virtual bool InitializeOptions( IActivityMonitor monitor, IReadOnlyList<(object Option, ImmutableConfigurationSection? Configuration)> options ) => true;

    internal bool SetOptions( IActivityMonitor monitor, IReadOnlyList<(object Option, ImmutableConfigurationSection? Configuration)> options )
    {
        if( !InitializeOptions( monitor, options ) ) return false;
        Options = options.Select( o => o.Option ).ToArray();
        return true;
    }

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
