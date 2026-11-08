# CK.AppIdentity.Engine

**The engine of CK.AppIdentity**: it generates the `PartyComponentOptionDirectory`, the options of each party component
owner type of the application, in their order (see
[the options of the party components](../CK.AppIdentity/PartyComponentOptions/README.md)). The application knows its
options without any reflection at runtime.

## How it works

[`PartyComponentOptionDirectoryImpl`](PartyComponentOptionDirectoryImpl.cs) implements the directory for each BinPath:

1. **The options** are the concrete classes of the BinPath that implement `IPartyComponentOption<TOwner>` (once), with
   exactly one `IPartyComponentOptionAttribute` (`[FeatureOption]` or `[PartyClientOption]`) whose kind of owner matches
   `TOwner`, and exactly one public constructor.
2. **The owners** are the concrete party components (`IPartyComponent`) of the BinPath to which at least one option
   applies (its owner type or a type derived from it).
3. **Each owner is a ResourceSpace island** (CK.ResourceSpace): its options are the allowed types, and the compatibility
   rule accepts the options of an owner type assignable from it (a relation to another class is ignored). The island
   orders the options by their topology attributes, and its non optional relations become the requirements of the
   options.
4. **The generated constructor** gives the directory the ordered options of each owner, with a factory per option that
   resolves its constructor parameters from the owner's services.

## Errors

| Case | Error |
|---|---|
| An option of more than one owner type | An option has one owner type. |
| An option without its attribute, or with more than one | It must be decorated with the attribute of its kind of owner. |
| An attribute of another kind of owner (`[PartyClientOption]` on an option of a feature driver) | Its attribute is the one of another kind of owner. |
| No public constructor, or more than one | It must have exactly one public constructor. |
| Two options of an owner with the same name (whatever their namespace) | They have the same name. |
| A cycle between the options of an owner | Reported by the ResourceSpace. |

## Requires

- `CK.AppIdentity` (the runtime directory), `CK.StObj.Engine` and `CK.ResourceSpace`.
