using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Marks an interface that party components implement and that other party components can require
/// (<c>[Requires&lt;TAbstraction&gt;]</c>): "a web application build" (implemented by an Angular client feature, a Vite
/// one...), "the TypeScript framework of a client".
/// <para>
/// An abstraction is unique in its scope by default (a client has at most one implementation of it): an interface
/// decorated with <c>[IsMultiple]</c> can be implemented more than once. A requirement on an abstraction means "present
/// in the scope": on a unique abstraction, exactly one implementation.
/// </para>
/// </summary>
public interface IPartyComponentAbstraction : IResourceGroup
{
}
