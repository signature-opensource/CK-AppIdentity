using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// A concrete party component type, as the engine computed it (see <see cref="PartyComponentDirectory"/>).
/// </summary>
public sealed class PartyComponentInfo
{
    /// <summary>
    /// Initializes a new <see cref="PartyComponentInfo"/>. Called by the generated code.
    /// </summary>
    /// <param name="componentType">The concrete component type.</param>
    /// <param name="requires">The required types (its <c>[Requires&lt;T&gt;]</c>): components or abstractions.</param>
    /// <param name="abstractions">The abstractions that it implements.</param>
    /// <param name="featureName">The name of the feature of this component (a feature driver, a client feature).</param>
    /// <param name="activation">The activation of this component.</param>
    /// <param name="side">The side of an asymmetric feature.</param>
    /// <param name="model">The description of the configuration model of the component (its <c>[Configuration&lt;TModel&gt;]</c>), if any.</param>
    public PartyComponentInfo( Type componentType,
                               IReadOnlyList<Type> requires,
                               IReadOnlyList<Type> abstractions,
                               string featureName,
                               TypeActivation activation,
                               FeatureSide side,
                               ConfigurationModelDescription? model = null )
    {
        Model = model;
        ComponentType = componentType;
        Requires = requires;
        Abstractions = abstractions;
        FeatureName = featureName;
        Activation = activation;
        Side = side;
    }

    /// <summary>
    /// Gets the concrete component type.
    /// </summary>
    public Type ComponentType { get; }

    /// <summary>
    /// Gets the types that this component requires (its <c>[Requires&lt;T&gt;]</c>): concrete party components (a feature
    /// driver) or abstractions (<see cref="IPartyComponentAbstraction"/>). "Only with it": the component can't be used
    /// without them.
    /// </summary>
    public IReadOnlyList<Type> Requires { get; }

    /// <summary>
    /// Gets the abstractions (<see cref="IPartyComponentAbstraction"/>) that this component implements.
    /// </summary>
    public IReadOnlyList<Type> Abstractions { get; }

    /// <summary>
    /// Gets the name of the feature of this component: "HttpEndpoint" for the <c>HttpEndpointFeatureDriver</c>, "NgApp" for
    /// the <c>NgAppClientFeature</c> (declared by its <see cref="PartyFeatureAttribute"/> or its <c>PartyClientFeatureAttribute</c>).
    /// The two sides of an asymmetric feature have the same name (see <see cref="Side"/>).
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Gets the activation of this component: <see cref="TypeActivation.Optional"/>, a party (or a client) has it only when
    /// it declares it; <see cref="TypeActivation.ByDefault"/>, it has it unless it turns it off; <see cref="TypeActivation.Required"/>,
    /// it always has it.
    /// </summary>
    public TypeActivation Activation { get; }

    /// <summary>
    /// Gets whether this component is optional: its <see cref="Activation"/> is <see cref="TypeActivation.Optional"/>.
    /// </summary>
    public bool IsOptional => Activation == TypeActivation.Optional;

    /// <summary>
    /// Gets the side of an asymmetric feature (see <see cref="PartyFeatureAttribute"/>).
    /// </summary>
    public FeatureSide Side { get; }

    /// <summary>
    /// Gets the description of the configuration model of this component (its <c>[Configuration&lt;TModel&gt;]</c>):
    /// a feature driver reads the model of each party (<see cref="ApplicationIdentityFeatureDriver.GetModel{TModel}(IParty)"/>),
    /// a client feature receives it from its driver. Null when the component reads its section itself.
    /// </summary>
    public ConfigurationModelDescription? Model { get; }

    /// <inheritdoc />
    public override string ToString() => ComponentType.Name;
}
