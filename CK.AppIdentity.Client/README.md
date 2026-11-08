# CK.AppIdentity.Client

**The clients of a party:** the applications that send commands to a local party on behalf of users (a browser
application served by the party, a local tool, a native application...). A client is not a party: it has no identity
key and it is not a remote. This package is the ground for every kind of client; the packages that bring a kind of
client build on it.

## A client is built by a package, and declared by a party

- **A package builds the clients of a kind.** Their type specializes [`PartyClient`](PartyClient.cs) (its type is its
  kind), and the package implements an [`IPartyClientFactory`](IPartyClientFactory.cs): it knows the names of the
  clients it builds (`ClientNames`), the features that its clients require on their party (`RequiredFeatures`, the
  drivers it receives as constructor parameters), and it creates a client from its configuration (`Create`).
- **A party declares the clients it uses**, in its "Local:Client" section: only the configuration says which party uses
  which client (one build can produce a client for a party and not for another). The declaration holds what the build
  can't know (the deployment facts, the options); everything else is derived from the build.

```jsonc
"CK-AppIdentity": {
  "Local": {
    "Client": {
      "App": {},                                   // Declared: its facts come from the package that builds it.
      "Admin": { /* deployment facts, options */ }
    }
  }
}
```

## The "Client" feature

[`ClientFeatureDriver`](ClientFeatureDriver.cs) creates the clients of the local parties: the root and the tenant
domains, those of the initial configuration and the dynamic ones (a remote has no clients here: its clients are known
by its description). It is **allowed by default**: `"DisallowFeatures": "Client"` makes a party without clients. The
clients of a party are its [`ClientFeature`](ClientFeature.cs) (`Clients`, `Find( name )`), in the order of their
declaration.

The rules fail closed:

| Case | Result |
|---|---|
| A declared client that no package builds | An error: the party's setup fails. |
| Two packages that build the same client name | An error. |
| A client whose package requires a feature that the party doesn't allow | An error. |
| A configured fact that the kind of client doesn't have | An error, reported by its factory. |
| A party that doesn't allow the feature but declares clients | An error. |
| A built client that a party doesn't declare | Not a client of this party: an information says what to add ("Add `Local:Client:{Name}` to its configuration"). |

The declarations are read on the party only: a "Defaults" section doesn't declare clients.

## The description of the party

[`PartyClientDescriptionContributor`](PartyClientDescriptionContributor.cs) announces the clients of the root party in
its description (`PartyDescription.ClientApplications`, see the description of a party in
[CK.AppIdentity](../CK.AppIdentity/README.md)): the clients that have a `PartyClient.Description` (a local tool has
none: it is not announced). The description contributors complete the root party's description only: the clients of
the tenant domains are not described.
