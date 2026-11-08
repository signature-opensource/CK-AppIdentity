using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// The attribute of an <see cref="IPartyComponentOption{TOwner}"/>: <see cref="FeatureOptionAttribute"/>
/// or the PartyClientOption attribute of CK.AppIdentity.Client. It is also the embedded resource attribute that the
/// ordering of the options requires.
/// </summary>
public interface IPartyComponentOptionAttribute : IEmbeddedResourceTypeAttribute
{
    /// <summary>
    /// Gets whether the option is enabled when its owner's configuration doesn't say: an opt-out option.
    /// Defaults to false: the options are opt-in.
    /// </summary>
    bool IsEnabledByDefault { get; }

    /// <summary>
    /// Gets the kind of owner of the options decorated by this attribute: the owner type of the option must be
    /// assignable to it.
    /// </summary>
    Type OwnerKind { get; }
}
