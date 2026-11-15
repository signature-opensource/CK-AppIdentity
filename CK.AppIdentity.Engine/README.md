# CK.AppIdentity.Engine

**The engine of CK.AppIdentity**: it generates the `PartyComponentOptionDirectory`, the options of each party component
owner type of the application, in their order (see
[the options of the party components](../CK.AppIdentity/PartyComponentOptions/README.md)), and the
`PartyComponentDirectory`, the requirements, the abstractions and the order of the party components. The application
knows them without any reflection at runtime.

## How it works

[`PartyComponentOptionDirectoryImpl`](PartyComponentOptionDirectoryImpl.cs) implements the directory for each BinPath:

1. **The options** are the concrete classes of the BinPath that implement `IPartyComponentOption<TOwner>` (once), with
   exactly one `IPartyComponentOptionAttribute` (`[FeatureOption]` or `[PartyClientOption]`) whose kind of owner matches
   `TOwner`, and exactly one public constructor: its `IConfiguration` parameter (a plain one, at most one) is the
   option's configuration, the other ones are services.
2. **The owners** are the concrete party components (`IPartyComponent`) of the BinPath to which at least one option
   applies (its owner type or a type derived from it).
3. **Each owner is a ResourceSpace island** (CK.ResourceSpace): its options are the allowed types, and the compatibility
   rule accepts the options of an owner type assignable from it (a relation to another class is ignored). The island
   orders the options by their topology attributes, and its non optional relations become the requirements of the
   options (`[Requires<T>]` on X: X requires T; `[RequiredBy<T>]` on X: T requires X).
4. **The generated constructor** gives the directory the ordered options of each owner, with a factory per option that
   passes its configuration and resolves the other constructor parameters from the owner's services.

## Errors

| Case | Error |
|---|---|
| An option of more than one owner type | An option has one owner type. |
| An option without its attribute, or with more than one | It must be decorated with the attribute of its kind of owner. |
| An attribute of another kind of owner (`[PartyClientOption]` on an option of a feature driver) | Its attribute is the one of another kind of owner. |
| No public constructor, or more than one | It must have exactly one public constructor. |
| More than one `IConfiguration` parameter | An option has one configuration. |
| A parameter of a derived configuration type (`IConfigurationSection`, `ImmutableConfigurationSection`) | The configuration of an option is a plain `IConfiguration`. |
| Two options of an owner with the same name (whatever their namespace) | They have the same name. |
| A cycle between the options of an owner | Reported by the ResourceSpace. |

## The party components

[`PartyComponentDirectoryImpl`](PartyComponentDirectoryImpl.cs) implements the `PartyComponentDirectory` for each BinPath,
from what [`PartyComponentAnalysis`](PartyComponentAnalysis.cs) computes (CK.Testing.AppIdentity builds the directory of a
test with it):

- **The components** are the concrete classes of the BinPath that implement `IPartyComponent` (the feature drivers, the
  client features).
- **Their requirements** are the type arguments of their `[Requires<...>]`: a component of the BinPath or an abstraction
  (an interface that extends `IPartyComponentAbstraction`). Anything else is an error.
- **Their abstractions** are the interfaces they implement that extend `IPartyComponentAbstraction`. An abstraction
  declares its cardinality: `[IsSingle]` (at most one implementation in a scope) or `[IsMultiple]` (it can be
  implemented more than once in a scope); declaring none is an error.
- **Their order:** a required component before the ones that require it, the implementations of a required abstraction
  before the ones that require it, otherwise by full name. A cycle is an error.
- **Their feature name and their optionality** follow the rules of the packages (`FeaturePackages`, CK.Engine.TypeSet):
  the closest `[PartyFeature]` of a feature driver (or `[ClientFeatureName]` of a client feature) in its base classes. The
  side of an asymmetric feature is the one of its `[PartyFeature]`: it requires an explicit name, the two sides agree on
  their optionality. The feature names are unique among the packages of the BinPath: a party feature and a client feature
  can't have the same name.

The "PartyClient" feature (CK.AppIdentity.PartyClient) checks the features of each client against it at start.

## The parties of the Apps ("ckonf.json")

When a "XXX.Builder" project has a `ckonf.json` (see CK.AppBuilder), the
[`AppIdentityAppBuilderContributor`](AppIdentityAppBuilderContributor.cs) reads the party of each App before the
engine runs:

- **its keys are directly in the App:** the keys of the root of an AppIdentity configuration
  (`ApplicationIdentityServiceConfiguration.RootKeys`: "FullName", "Local", "Parties", "Defaults"...), next to the other
  sections of the host (`Logging`...). They are projected in the "CK-AppIdentity" section of the host's
  `appsettings.json`. An App without any of them is not a party.
- **the party is validated at build time** (`ApplicationIdentityServiceConfiguration`, the App name is the default
  party name, the host project the base path): an invalid configuration is a build error.
- **each local party and each client of the root party has its type set**, a `TypeSetConfiguration` of the App's
  BinPath (computed by the engine as a subset of its types, `IRunningBinPathGroup.TypeSets`): the `Types` and
  `ExcludedTypes` of its section (`Local` for a party, `Parties[i]:Local` for a tenant domain, `Local:PartyClient:{Name}`
  for a client), compile time keys (not projected). A party's set is named after the party (`$Front`), a client's after
  the client (`MiniApp`). A type is its assembly qualified name, or an object whose `Name` is (the other keys are the
  projection's: `{ "Name": "...", "Optional": true }`). The exclusions of the root party apply to its clients: a client
  that adds a type that its party excludes is an error.
- **its root party's clients** ("Local:PartyClient:{Name}") are exposed (`Clients`, their `Features` by name, their
  `TypeSet`) to the contributors that build them: the contributor of a build feature ("NgApp") calls
  `DeclaredClient.SetHandled( "NgApp" )`, a client has one build feature. A declared client that no contributor builds
  is an error: "No package builds the client...".

## Requires

- `CK.AppIdentity` (the runtime directory), `CK.StObj.Engine`, `CK.Engine` (the App builder contract) and
  `CK.ResourceSpace`.
