using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Marks a configurable component of a party: a feature driver (<see cref="IApplicationIdentityFeatureDriver"/>) or a
/// feature of a client of a party. A party component is configured in its party's configuration and can have options
/// contributed by other packages.
/// <para>
/// A party component is a resource group: the topology attributes declare its requirements (<c>[Requires&lt;T&gt;]</c>:
/// another party component, or an abstraction, see <see cref="IPartyComponentAbstraction"/>).
/// </para>
/// </summary>
public interface IPartyComponent : IResourceGroup
{
}