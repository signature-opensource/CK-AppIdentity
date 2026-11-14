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
    public PartyComponentInfo( Type componentType, IReadOnlyList<Type> requires, IReadOnlyList<Type> abstractions )
    {
        ComponentType = componentType;
        Requires = requires;
        Abstractions = abstractions;
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

    /// <inheritdoc />
    public override string ToString() => ComponentType.Name;
}
