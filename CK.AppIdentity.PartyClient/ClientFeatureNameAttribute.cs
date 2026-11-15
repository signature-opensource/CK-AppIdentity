using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// Names the client features: it decorates <see cref="ClientFeature"/>, the name of a client feature is its type name
/// without the "ClientFeature" suffix (<c>NgAppClientFeature</c> is "NgApp"). A client feature is optional: a client has
/// it only when it declares it (its key). The engine reads it (see <see cref="PartyComponentInfo.FeatureName"/>).
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class ClientFeatureNameAttribute : Attribute, INamedFeatureAttribute, IOptionalResourceGroupAttribute
{
    string? INamedFeatureAttribute.Name => null;

    string? INamedFeatureAttribute.TypeNameSuffix => ClientFeature.TypeNameSuffix;

    bool IOptionalResourceGroupAttribute.IsOptional => true;
}
