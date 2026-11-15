using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Marks an interface that party components implement and that other party components can require
/// (<c>[Requires&lt;TAbstraction&gt;]</c>): "a web application build" (implemented by an Angular client feature, a Vite
/// one...), "the TypeScript framework of a client".
/// <para>
/// An abstraction declares its cardinality: <c>[IsSingle]</c> (at most one implementation in its scope: a client has at
/// most one) or <c>[IsMultiple]</c> (it can be implemented more than once); an abstraction that declares none is an
/// error. A requirement on an abstraction means "present in the scope": on a single abstraction, exactly one
/// implementation.
/// </para>
/// </summary>
public interface IPartyComponentAbstraction : IResourceGroup
{
}
