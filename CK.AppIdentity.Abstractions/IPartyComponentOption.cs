using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// An option of a party component (the party component level of <see cref="IConfigurableOption{TOwner}"/>): an option of
/// a feature driver (decorated with <see cref="PartyFeatureOptionAttribute"/>)
/// or of a client feature (decorated with the PartyClientFeatureOption attribute of CK.AppIdentity.PartyClient). It applies to its
/// <typeparamref name="TOwner"/> and to the owners derived from it.
/// <para>
/// An option is a plain class (not an auto service) that declares its data: its single public constructor reads its
/// configuration (its <c>IConfiguration</c> parameter, null when the option is enabled by default or by <c>true</c>) into
/// typed properties, and throws on an invalid value; its other parameters are services. Its owner creates it and holds it
/// (a feature driver per party, a client feature its own ones). What applying an option means is the owner's: an owner
/// that acts on its options defines their base.
/// </para>
/// <para>
/// The options of an owner are ordered by the engine: the topology attributes between options (<c>[Requires]</c>,
/// <c>[RequiredBy]</c> and their optional forms) order them; a non optional relation also means "only with it".
/// Its name is declared by its attribute (an option is a configurable type), and it is configured in its owner's section (see
/// <c>PartyComponentOptionDirectory</c> in CK.AppIdentity).
/// </para>
/// </summary>
/// <typeparam name="TOwner">The party component type (or a base of it) to which this option applies.</typeparam>
public interface IPartyComponentOption<TOwner> : IConfigurableOption<TOwner> where TOwner : IPartyComponent
{
}
