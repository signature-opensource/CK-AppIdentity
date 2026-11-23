using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// The attribute of an <see cref="IPartyComponentOption{TOwner}"/>: <see cref="PartyFeatureOptionAttribute"/>
/// or the PartyClientFeatureOption attribute of CK.AppIdentity.PartyClient. It is the party component level of the
/// <see cref="IConfigurableOptionAttribute"/> (its name is its key in the section of its owner, its activation makes it
/// opt-in or opt-out, its <see cref="IConfigurableOptionAttribute.OwnerKind"/> is the kind of party component that it
/// decorates the options of).
/// </summary>
public interface IPartyComponentOptionAttribute : IConfigurableOptionAttribute
{
}
