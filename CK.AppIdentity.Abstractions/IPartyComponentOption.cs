using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// An option of a party component: an option of a feature driver (decorated with <see cref="FeatureOptionAttribute"/>)
/// or of a party client (decorated with the PartyClientOption attribute of CK.AppIdentity.PartyClient). It applies to its
/// <typeparamref name="TOwner"/> and to the owners derived from it.
/// <para>
/// An option is a plain class (not an auto service): it is created from the services of its owner when the owner applies
/// its options, then forgotten. What applying an option means is the owner's: an owner defines its own base for its options.
/// </para>
/// <para>
/// The options of an owner are ordered by the engine: the topology attributes between options (<c>[Requires]</c>,
/// <c>[RequiredBy]</c> and their optional forms) order them; a non optional relation also means "only with it".
/// Its name is its type name without its "Option" suffix, and it is configured in its owner's section (see
/// <c>PartyComponentOptionDirectory</c> in CK.AppIdentity).
/// </para>
/// </summary>
/// <typeparam name="TOwner">The party component type (or a base of it) to which this option applies.</typeparam>
public interface IPartyComponentOption<TOwner> : IResourceGroup where TOwner : IPartyComponent
{
}
