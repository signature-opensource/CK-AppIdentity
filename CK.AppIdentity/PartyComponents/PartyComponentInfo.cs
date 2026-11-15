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
    /// <param name="featureName">The name of the feature of this component (a feature driver, a client feature), if any.</param>
    /// <param name="isOptional">Whether this component is optional (a party or a client has it only when it declares it).</param>
    /// <param name="side">The side of an asymmetric feature.</param>
    public PartyComponentInfo( Type componentType,
                               IReadOnlyList<Type> requires,
                               IReadOnlyList<Type> abstractions,
                               string? featureName,
                               bool isOptional,
                               FeatureSide side )
    {
        ComponentType = componentType;
        Requires = requires;
        Abstractions = abstractions;
        FeatureName = featureName;
        IsOptional = isOptional;
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
    /// the <c>NgAppClientFeature</c>. The two sides of an asymmetric feature have the same name (see <see cref="Side"/>).
    /// Null for a component that is not a feature.
    /// </summary>
    public string? FeatureName { get; }

    /// <summary>
    /// Gets whether this component is optional: a party (or a client) has it only when it declares it. Otherwise it is
    /// allowed by default (it can be turned off).
    /// </summary>
    public bool IsOptional { get; }

    /// <summary>
    /// Gets the side of an asymmetric feature (see <see cref="PartyFeatureAttribute"/>).
    /// </summary>
    public FeatureSide Side { get; }

    /// <inheritdoc />
    public override string ToString() => ComponentType.Name;
}
