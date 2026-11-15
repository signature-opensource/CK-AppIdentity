# CK.AppIdentity.PartyClient

**The clients of a party:** the applications that send commands to a local party on behalf of users (a browser
application served by the party, a mobile application, a local tool...). A client is not a party: it has no identity key
and it is not a remote. This package is the ground for every client: the packages that bring a client feature build on
it.

## A client is a bag of client features, declared by a party

A client is to its features what a party is to its features: [`PartyClient`](PartyClient.cs) is a container (its
`Name`, its `Party`, its `Features`), and what it is comes from its features: "NgApp" (an Angular application, CK.TS.Angular),
"Hosting" (served by its party, CK.TS.AspNet.Clients)...

- **A package brings a client feature:** a class that specializes [`ClientFeature`](ClientFeature.cs), and its driver, a
  [`ClientFeatureDriver<T>`](IClientFeatureDriver.cs) (an `[IsMultiple]` auto service) that creates the feature of a
  client from its configuration. The name of a feature is its type name without the "ClientFeature" suffix
  (`NgAppClientFeature` is "NgApp", `[ClientFeatureName]` on `ClientFeature`): the engine computes it into the
  `PartyComponentDirectory` (`ClientFeature.Name`).
- **A party declares its clients**, in its "Local:PartyClient" section: only the configuration says which party has which
  client. The keys of a client are its features (their section is their configuration) and its type set:

```jsonc
"Local": {
  "PartyClient": {
    "App": {
      "Types": [ "..." ], "ExcludedTypes": [ "..." ],   // Its type set: compile time keys, ignored at run time.
      "NgApp": true,                                    // A feature without configuration: true (an empty object is nothing).
      "Hosting": { "RequestPath": "/", "DevServerPort": 4200 }
    }
  }
}
```

## The requirements of a feature

A client feature declares its requirements with `[Requires<T>]` (a party component is a resource group, see
`IPartyComponent`):

- **a feature of its party:** `[Requires<HttpEndpointFeatureDriver>]`: the party must allow it;
- **an abstraction implemented by another feature of the client:** `[Requires<IWebAppBuild>]`. An abstraction is an
  interface that extends `IPartyComponentAbstraction` and declares its cardinality: `[IsSingle]` (a client has at most
  one implementation of it) or `[IsMultiple]`. A requirement on an abstraction means "present": on a single
  abstraction, exactly one implementation. [`IWebAppBuild`](IWebAppBuild.cs) (the web build of a client: its `ProjectPath`, its
  `OutputPath`, `GetDevServerStart`) is the one of this package.

The engine reads them (no runtime reflection): the `PartyComponentDirectory` of CK.AppIdentity has the requirements, the
abstractions and the order of every party component (a component after the ones it requires, after all the
implementations of a required abstraction).

## The "PartyClient" feature

[`PartyClientFeatureDriver`](PartyClientFeatureDriver.cs) creates the clients of the local parties: the root and the tenant
domains, those of the initial configuration and the dynamic ones (a remote has no clients here: its clients are known
by its description). It is **allowed by default**: `"PartyClient": false` makes a party without clients. The
clients of a party are its [`PartyClientFeature`](PartyClientFeature.cs) (`Clients`, `Find( name )`), in the order of their
declaration. For each client, it finds the driver of each feature, checks the requirements
(`PartyComponentDirectory.CheckScope`), creates the features in their order, then their options.

The rules fail closed:

| Case | Result |
|---|---|
| A key of a client that is neither a client feature of the application nor its type set | An error: the party's setup fails. |
| A client without feature | An error. |
| A feature that requires a feature that the party doesn't have | An error. |
| A feature that requires an abstraction that no feature of the client implements | An error. |
| Two features of a client that implement a unique abstraction | An error. |
| A key of a feature that the feature doesn't have | An error, reported by its driver. |

The declarations are read on the party only: a "Defaults" section doesn't declare clients.

## The options of a client feature

A package can add an option to a client feature: a class decorated with `[PartyClientOption]`
([`PartyClientOptionAttribute`](PartyClientOptionAttribute.cs)) that implements `IPartyComponentOption<TFeature>`. It
applies to `TFeature` and to the features derived from it. See
[the options of the party components](../CK.AppIdentity/PartyComponentOptions/README.md).

- **The feature driver of the party selects them:** once a feature is created, `PartyClientFeatureDriver` selects the
  options of its type from its section (their key is their name), creates them from the services and gives them to the
  feature: `ClientFeature.Options`, in their order.
- **The feature applies them:** `ClientFeature.InitializeOptions( monitor, options )` is called once with the options and
  their configuration section. It accepts them by default.
- **A driver ignores the option keys** of the section it receives: it refuses only a key that is neither one of its own
  nor an option name of its feature (`PartyComponentOptionDirectory.IsOptionName`).

```jsonc
"Hosting": { "StaticFiles": { "AssetsMaxAge": "01:00:00" } }
```

## The description of the party

[`PartyClientFeatureDriver`](PartyClientFeatureDriver.cs) announces the clients of each local party (the root and the tenant
domains) in its description (`PartyDescription.ClientApplications`, see the description of a party in
[CK.AppIdentity](../CK.AppIdentity/README.md)). The description of a client (`PartyClient.Description`) is assembled from
its features (`ClientFeature.ContributeDescription`): its request path (the feature that serves it) and its facts. A
client that its party doesn't serve (no request path) is not announced.

## The facts of a client

A type that a client is built from states facts about it with
[`[PartyClientFact( key, value )]`](PartyClientFactAttribute.cs) (repeatable, not inherited, the value is optional): a
TypeScript package that is an approval page states `[PartyClientFact( "Enlistment", "app-identity/enlist" )]` (the path of
the page), an administration application `[PartyClientFact( "Admin" )]`. The engine that builds a client unions the facts
of the types the client is built from, once per build (the TypeScript engine does it for the client applications, from
their selected packages), and its build feature contributes them to the description. The result is a
[`ClientApplicationFacts`](../CK.AppIdentity.Abstractions/ClientApplicationFacts.cs), a multimap (a key has a set of
values, the union is associative, commutative and idempotent): the `Facts` of the client's description, announced with
it. Each consumer decides what it accepts for a key. Nothing is configured, nothing is computed at run time: what a
client is comes from what it is built from.
