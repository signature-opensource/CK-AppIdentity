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
  client from its configuration. A client feature declares its name and its activation:
  `[PartyClientFeature( "NgApp", TypeActivation.Optional )]` ([`PartyClientFeatureAttribute`](https://github.com/signature-opensource/CK-Abstractions/blob/stable/CK.AppBuilder.Abstractions/PartyClient/PartyClientFeatureAttribute.cs),
  not inherited: each concrete client feature declares its own). The engine reads it into the `PartyComponentDirectory`
  (`ClientFeature.Name`); the names are unique among the client features. A client has an `Optional` feature when it
  declares it (`true` or its section), a `ByDefault` one unless it says `false`, a `Required` one always (`false` is an
  error); a redundant `true` or `false` is a warning.
- **A party declares its clients**, in its "Local:PartyClient" section: only the configuration says which party has which
  client. The keys of a client are its features (their section is their configuration) and its type set:

```jsonc
"Local": {
  "PartyClient": {
    "App": {
      "Types": [ "..." ], "ExcludedTypes": [ "..." ],   // Its type set: compile time keys, ignored at run time.
      "NgApp": { "RequestPath": "/", "DevServerPort": 4200 },  // Its compile time keys: ignored at run time.
      "Hosting": true                                   // A feature without configuration: true (an empty object is nothing).
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
(`PartyComponentDirectory.CheckScope`), creates the features in their order then, once they are all created, their
options.

The rules fail closed:

| Case | Result |
|---|---|
| A key of a client that is neither a client feature of the application nor its type set | An error: the party's setup fails. |
| A client without feature | An error. |
| A feature that requires a feature that the party doesn't have | An error. |
| A feature that requires an abstraction that no feature of the client implements | An error. |
| Two features of a client that implement a unique abstraction | An error. |
| A key of a feature that is neither a key of its model nor one of its options nor a key of its compile time model | An error. |

The declarations are read on the party only: a "Defaults" section doesn't declare clients.

## A client feature with a configuration model

A client feature may declare its configuration model (`[Configuration<TModel>]` on the feature type): its driver
specializes `ClientFeatureDriver<T, TModel>` and creates the feature from the model. `PartyClientFeatureDriver` reads it
from the feature's section (a bare `true` is the defaults of the model), the names of the options of the feature and the
keys of its compile time model reserved: an unknown key, a missing required one or an invalid value is an error before
the driver is called.

**No model, no configuration:** a feature without model (its driver specializes `ClientFeatureDriver<T>`) has no key of
its own: its section has only its options and the keys of its compile time model, any other key is an error. A driver
never checks its keys.

**The compile time model** of a feature (`[CompileConfiguration<TModel>]`, read by the App builder: see
CK.AppIdentity.Engine) is read by the build: its keys are accepted and ignored at run time (`PartyComponentInfo.CompileKeys`).

## The options of a client feature

A package can add an option to a client feature: a class decorated with `[PartyClientFeatureOption( "Name", TypeActivation.X )]`
([`PartyClientFeatureOptionAttribute`](https://github.com/signature-opensource/CK-Abstractions/blob/stable/CK.AppBuilder.Abstractions/PartyClient/PartyClientFeatureOptionAttribute.cs)) that implements `IPartyComponentOption<TFeature>`. It
applies to `TFeature` and to the features derived from it. See
[the options of the party components](../CK.AppIdentity/PartyComponentOptions/README.md).

- **The feature driver of the party creates them:** once every feature of the client is created,
  `PartyClientFeatureDriver` selects the options of each feature from its section (their key is their name), creates
  them (each one reads its configuration in its constructor: an invalid one throws and makes the client an error) and
  gives them to the feature: `ClientFeature.Options`, in their order, and `ClientFeature.GetOption<T>()`.
- **An option can require a feature:** a feature of the party (`[Requires<XXXFeatureDriver>]`, allowed by the party) or
  another feature of the client (`[Requires<HostingClientFeature>]`): it is enabled only with it.
- **The feature applies them:** applying an option is the feature's business (the base type of its options).
- **Before the setup:** `PartyClientFeatureDriver.TryFindOptionModels` finds the clients whose feature enables an option
  and reads the option's model from the configuration, for what must be known before the host starts (the window of a
  desktop application).

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
[`[PartyClientFact( key, value )]`](https://github.com/signature-opensource/CK-Abstractions/blob/stable/CK.AppBuilder.Abstractions/PartyClient/PartyClientFactAttribute.cs) (repeatable, not inherited, the value is optional): a
TypeScript package that is an approval page states `[PartyClientFact( "Enlistment", "app-identity/enlist" )]` (the path of
the page), an administration application `[PartyClientFact( "Admin" )]`. The engine that builds a client unions the facts
of the types the client is built from, once per build (the TypeScript engine does it for the client applications, from
their selected packages), and its build feature contributes them to the description. The result is a
[`ClientApplicationFacts`](../CK.AppIdentity.Abstractions/ClientApplicationFacts.cs), a multimap (a key has a set of
values, the union is associative, commutative and idempotent): the `Facts` of the client's description, announced with
it. Each consumer decides what it accepts for a key. Nothing is configured, nothing is computed at run time: what a
client is comes from what it is built from.
