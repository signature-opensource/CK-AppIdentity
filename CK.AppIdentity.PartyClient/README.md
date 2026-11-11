# CK.AppIdentity.PartyClient

**The clients of a party:** the applications that send commands to a local party on behalf of users (a browser
application served by the party, a local tool, a native application...). A client is not a party: it has no identity
key and it is not a remote. This package is the ground for every kind of client; the packages that bring a kind of
client build on it.

## A client is built by a package, and declared by a party

- **A package builds the clients of a kind.** Their type specializes [`PartyClient`](PartyClient.cs) (its type is its
  kind), and the package implements an [`IPartyClientFactory`](IPartyClientFactory.cs): it knows the names of the
  clients it builds (`ClientNames`), the features that its clients require on their party (`RequiredFeatures`, the
  drivers it receives as constructor parameters), and it creates a client from its configuration (`Create`).
- **A party declares the clients it uses**, in its "Local:PartyClient" section: only the configuration says which party uses
  which client (one build can produce a client for a party and not for another). The declaration holds what the build
  can't know (the deployment facts, the options); everything else is derived from the build.

```jsonc
"CK-AppIdentity": {
  "Local": {
    "PartyClient": {
      "App": {},                                   // Declared: its facts come from the package that builds it.
      "Admin": { /* deployment facts, options */ }
    }
  }
}
```

## The "PartyClient" feature

[`PartyClientFeatureDriver`](PartyClientFeatureDriver.cs) creates the clients of the local parties: the root and the tenant
domains, those of the initial configuration and the dynamic ones (a remote has no clients here: its clients are known
by its description). It is **allowed by default**: `"DisallowFeatures": "PartyClient"` makes a party without clients. The
clients of a party are its [`PartyClientFeature`](PartyClientFeature.cs) (`Clients`, `Find( name )`), in the order of their
declaration.

The rules fail closed:

| Case | Result |
|---|---|
| A declared client that no package builds | An error: the party's setup fails. |
| Two packages that build the same client name | An error. |
| A client whose package requires a feature that the party doesn't allow | An error. |
| A configured fact that the kind of client doesn't have | An error, reported by its factory. |
| A party that doesn't allow the feature but declares clients | An error. |
| A built client that a party doesn't declare | Not a client of this party: an information says what to add ("Add `Local:PartyClient:{Name}` to its configuration"). |

The declarations are read on the party only: a "Defaults" section doesn't declare clients.

## The options of a client

A package can add an option to a kind of client: a class decorated with `[PartyClientOption]`
([`PartyClientOptionAttribute`](PartyClientOptionAttribute.cs)) that implements `IPartyComponentOption<TClient>`. It
applies to `TClient` and to the clients derived from it. See
[the options of the party components](../CK.AppIdentity/PartyComponentOptions/README.md).

- **The feature selects them, for every kind of client:** once a factory has created a client, `PartyClientFeatureDriver`
  selects the options of the client's type from its `Local:PartyClient:{Name}` section (their key is their name), creates
  them from the services and gives them to the client: `PartyClient.Options`, in their order.
- **The client applies them:** `PartyClient.InitializeOptions( monitor, options )` is called once with the options and
  their configuration section. It accepts them by default; a kind of client that has options checks there that they are
  the ones it expects (a base type of its own) and initializes them.
- **A factory ignores the option keys** of the section it receives: it refuses only a key that is neither one of its
  own nor an option name of its client type (`PartyComponentOptionDirectory.IsOptionName`).

```json
"PartyClient": {
  "Admin": { "Caching": true }
}
```

## The description of the party

[`PartyClientFeatureDriver`](PartyClientFeatureDriver.cs) announces the clients of each local party (the root and the tenant
domains) in its description (`PartyDescription.ClientApplications`, see the description of a party in
[CK.AppIdentity](../CK.AppIdentity/README.md)): the clients that have a `PartyClient.Description` (a local tool has
none: it is not announced).

## The facts of a client

A type that a client is built from states facts about it with
[`[PartyClientFact( key, value )]`](PartyClientFactAttribute.cs) (repeatable, not inherited, the value is optional): a
TypeScript package that is an approval page states `[PartyClientFact( "Enlistment", "app-identity/enlist" )]` (the path of
the page), an administration application `[PartyClientFact( "Admin" )]`. The engine that builds a kind of client unions
the facts of the types each client is built from, once per build (the TypeScript engine does it for the client
applications, from their selected packages). The result is a
[`ClientApplicationFacts`](../CK.AppIdentity.Abstractions/ClientApplicationFacts.cs), a multimap (a key has a set of
values, the union is associative, commutative and idempotent): the `Facts` of the client's description, announced with
it. Each consumer decides what it accepts for a key. Nothing is configured, nothing is computed at run time: what a
client is comes from what it is built from.
