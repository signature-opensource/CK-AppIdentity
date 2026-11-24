The engine of CK.AppIdentity: it generates the PartyComponentOptionDirectory (the ordered options of each party component
owner type) and the PartyComponentDirectory (the requirements and the order of the party components).

The options are the `IPartyComponentOption<TOwner>` of the application, decorated with `[PartyFeatureOption]` or
`[PartyClientFeatureOption]`, ordered by their topology attributes, with a factory per option (see
[the options of the party components](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/PartyComponentOptions/README.md)).

See the [README](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity.Engine/README.md).
