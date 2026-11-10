# CK.AppIdentity

The implementation of the model: one hosted singleton, four party classes behind the four interfaces,
and a micro agent that owns every mutation.

> ℹ️ Read [CK.AppIdentity.Configuration](../CK.AppIdentity.Configuration/README.md) and then
> [CK.AppIdentity.Abstractions](../CK.AppIdentity.Abstractions/README.md) first: this package adds no
> concept, it realizes theirs.

## The service is a hosted lifecycle service.

[`ApplicationIdentityService`](ApplicationIdentityService.cs) is declared as:

```csharp
public sealed partial class ApplicationIdentityService : LocalParty,
                                                         IApplicationIdentityService,
                                                         IHostedLifecycleService,
                                                         IAsyncDisposable
```

Three things follow from that signature, and they are the whole shape of the package.

**It is the local party, not a registry of parties.** It derives from `LocalParty`, so the application's
own identity is the root object rather than an entry inside one. `Parties` are its remotes and tenant
domains; `AllRemotes` and `AllParties` flatten the tenant domains into one enumeration.

**It participates in the host lifecycle.** `IHostedLifecycleService` is the extended interface, not just
`IHostedService`, which is what lets the identity be fully initialized before anything that depends on
it starts: `StartingAsync` runs before any plain `IHostedService.StartAsync` in the application.

Both entry points lead to the same `DoStartAsync`, and the comment says why that duplication is
deliberate - *"this enables pseudo host code to use `IHostedService.StartAsync` transparently"*. So
code without a real host can cast to `IHostedService`, call `StartAsync`, then await
`InitializationTask`. `StartAndInitializeAsync()` does both in one call and is safe to call several
times.

`InitializationTask` is the handle for anything that must wait: when it completes successfully, every
configured feature is initialized and available.

Then the **static features** start, outside the agent's loop: `StartedAsync` (or `StartAndInitializeAsync`) calls their
`OnHostStartedAsync` once the initialization is done, and `StoppingAsync` (or `DisposeAsync`) calls their
`OnHostStoppingAsync` before the agent stops. A code that calls only `IHostedService.StartAsync` doesn't start them.
See [the static features](Features/README.md#static-features).

**It disposes the graph.** `IAsyncDisposable` stops the static features, then tears down the features of every party, in
order.

## Every mutation goes through one agent.

The implementation is thread-safe, and it is thread-safe by construction rather than by locking: a micro
agent serializes everything that changes. [`AppIdentityAgent`](AppIdentityAgent.cs) handles the
initialization, the lifetime of dynamic parties, the setup and teardown of features, and the disposal of
the whole service.

Its base class [`MicroAgent`](MicroAgent.cs) is public on purpose, and its summary says why:

> It is rather basic but enough for our needs here and may be reused by
> `ApplicationIdentityFeatureDriver` if needed.

So a feature that needs its own serialization point does not have to invent one. What `MicroAgent`
guarantees is narrow and worth knowing before reusing it: it always accepts jobs, it starts once and
**may refuse to start**, and once started it always runs - errors are logged and it continues - until
`SendStop` is called.

## Adding and destroying at runtime.

A configured party is immutable and cannot be destroyed. A dynamic one is added through the same
configuration shape, as a `MutableConfigurationSection` built in code:

```csharp
Task<AddedDynamicParties?> AddPartiesAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration );
Task<ITenantDomainParty?> AddTenantDomainAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration );
```

Both return null on failure rather than throwing - the monitor carries the reason. And both are on the
service; `ILocalParty.AddRemoteAsync` is the equivalent one level down, which is what makes a tenant
domain extensible without touching the root.

Destruction is `IOwnedParty.DestroyAsync`, and `SetDestroyed` is its two-phase companion: it marks the
party so that no one else starts working with it, and returns whether this call is the one that won the
race.

## What observers get.

Three `PerfectEvent` are exposed rather than plain events, so a handler can be asynchronous and the
sender can await it:

| Event | Raised on |
|-------|-----------|
| `ILocalParty.AnyRemoteChanged` | a remote added to or destroyed on that party |
| `IApplicationIdentityService.AnyPartyChanged` | any owned party anywhere in the graph |
| `IApplicationIdentityService.Heartbeat` | the periodic tick, carrying an `int` |

`SystemClock` is exposed beside them, which is what makes the heartbeat testable: a test substitutes the
clock instead of waiting.

## The features are the point.

The objects above do nothing but be what they are. Everything a party actually *does* - managing the key
store of a remote, exposing a third-party API, carrying a transport - arrives as a feature, and features
are described in [`Features/README.md`](Features/README.md), which also covers the "Package First"
approach and the feature configuration.

Other packages can add options to a feature driver (or to any party component): the
[`PartyComponentOptionDirectory`](PartyComponentOptions/README.md) knows them, in their order, and selects those that a
party's configuration enables.

[`BasicTrampoline`](Impl/BasicTrampoline/README.md) documents the helper the feature setup uses to run
ordered, possibly-failing steps.

## The running status of a development party.

The "DevRunningStatus" feature ([`DevRunningStatusFeatureDriver`](DevRunningStatus/DevRunningStatusFeatureDriver.cs), a
static feature allowed by default) writes `DevRunningStatus.json` in the root party's folder of its store
(`IParty.SharedFileStore`) while the host runs. It does this only when the environment is `#Dev` **and** the store is
the `#Dev/Store` of the development solution (`ApplicationIdentityServiceConfiguration.IsDevStore`: a host in Development
that doesn't configure its `StoreRootPath`).
Otherwise it is dormant: it checks once and does nothing more. `DisallowFeatures` switches it off.
- **The file is written** when the host has started (`OnHostStartedAsync`), rewritten after a change (batched over
  200 ms), and deleted when the host stops. The write is atomic: a temporary file, then a rename.
- **Its core properties:** `FullName`, `ProcessId`, `ProcessStartTime` (with the pid, they detect a stale file),
  `ContentRootPath`, `Ready` and the `Remotes` (`FullName`, `Outgoing`).
- **The packages contribute their own properties** with an `IDevRunningStatusContributor` (CK.AppIdentity.Abstractions):
  for instance, the transport (the remotes' availability), the HTTP endpoints, the client applications. `Ready` is
  true when every contributor is ready. A section can hold a `Pending` array: what an operator must do for the party
  (an approval, for instance), printed by the launcher; what waits for an operator doesn't make a party not ready. A feature driver implements it itself (explicitly: it doesn't add to its
  public API), as the transport and the HTTP endpoints do; a separate contributor is for a package that has state of
  its own and no driver (the client applications).
- **A contributor stays dormant until the writer activates it:** it subscribes to nothing and computes nothing before.

The launcher of the solution, `#Dev/Start.ps1` (see [CK.AppIdentity.Hosting](../CK.AppIdentity.Hosting/README.md)),
reads these files.

## The description of a party.

A party states what it is to the others: its public URL, its client applications, its roles and its encryption key (`IParty.Description`,
a `PartyDescription` of CK.AppIdentity.Abstractions). The AuthCenter, for instance, builds the links of its mails with the
URL of the party through which the user acts, and derives the origins it accepts from the URLs of its remotes.

The "PartyDescription" feature ([`PartyDescriptionFeatureDriver`](PartyDescription/PartyDescriptionFeatureDriver.cs), a
static feature allowed by default) sets it:
- **A local party's public URL is its `Local:PublicUrl`** configuration: an absolute `http` or `https` URL without path,
  query nor fragment (`PartyDescription.TryParsePublicUrl`); an invalid one fails the start.
- **The feature drivers complete the description of every local party** (the root and the tenant domains alike): a
  driver overrides `ApplicationIdentityFeatureDriver.ContributeDescription( party, builder )`. The HTTP endpoints give
  the public URL of the root party when `Local:PublicUrl` is not configured, the "PartyClient" feature gives the client
  applications of each party. It is computed once all the drivers have set up the party, in their order, then each
  time a driver signals a change (`OnDescriptionChangedAsync`): `ILocalParty.DescriptionChanged` is raised when it
  changed.
- **It is built in two phases.** The first one (`ContributeDescription`, a `PartyDescription.Builder`) states the
  facts: the public URL, the client applications, the data announcements (`SetData( name, value )`), the routes
  that the party hosts (`HostRoute( name, target )`) and the items it states along a route (`Collect( name, value )`).
  The second one (`ContributeDerivedDescription`, a `PartyDescription.DerivedBuilder`) reads the first one and sets
  what derives from it: the routes (`SetRoute`) and the collected items (`SetCollected`).
- **Data announcements and routes are an extensible part of the description.** `PartyDescription.Data` holds named
  strings (announced, compared, persisted). `PartyDescription.Routes` holds named [`PartyRoute`](../CK.AppIdentity.Abstractions/PartyRoute.cs)s:
  the route to the party that hosts something (its `Target`, a mere string: an URL, the name of a command...) and the
  `Path` of the parties to it. A party finds its own name in no path it uses (no loop), takes the shortest one
  (`SelectBest`) and extends it with its name (`Extend`). The routes are announced and compared, never persisted: a
  route counts only from a connected remote.
- **Items travel a route toward its host.** `PartyDescription.Collected` holds, per route name, the
  [`CollectedItem`](../CK.AppIdentity.Abstractions/CollectedItem.cs)s of the party and the ones its remotes announce on
  their way to the host of the route: an opaque `Value` (a mere string, like a route's `Target`) and the `Path` from
  the party that states it (the `Holder`) to the announcing one. A party accepts an item from the remote that is the
  last of its path (`TryAccept`: no loop, shorter than `PartyRoute.MaxLength`) and appends its name (`Extend`): the
  reversed path is the way back to the holder. At most `CollectedItem.MaxCount` items per route name, each value of at
  most `CollectedItem.MaxValueLength` characters. Announced and compared, never persisted, like the routes.
- **The encryption key of the party** (`PartyDescription.EncryptionKey`, a [`PartyEncryptionKey`](../CK.AppIdentity.Abstractions/PartyEncryptionKey.cs)):
  its public key and the signature of this public key by the party's identity key, set by the key management
  (`PartyDescription.Builder.EncryptionKey`). Only bytes here: its verification and its use belong to the key
  management. Short-lived, it is announced and compared, never persisted (like the routes).
- **`ILocalParty.RemoteDescriptionChanged`** is raised when a remote announces another description (by the agent's
  loop: `IRemoteParty.SetDescription` stays synchronous). The last known description read at set up raises nothing.
- **The roles of a local party are the [asymmetric features](Features/README.md#asymmetric-features) it is** (their
  Local side is installed and its `Local` allows them). A local party that relays an asymmetric feature to a remote
  checks that the remote announces it. No other feature name is announced: the description is what the other parties
  act on.
- **A remote's description is its last announcement** (`IRemoteParty.SetDescription`, called by the channel), persisted
  in the remote's folder of the shared store (`PartyDescription.json`: it is the remote's own statement, the same for
  every local party) and read back when the remote is set up, so that it is known while the remote is not connected. A
  change of its public URL is logged as a warning.

The URL of a client application is derived, never stored: `PartyDescription.GetClientApplicationUrl( name )` (the
default one, served at `/`, when the name is null).

**The caller party** of a call (the `CallerPartyFullName` of its authentication information) is found with
`CallerPartyExtensions.FindCallerParty` on the local party that received it: the local party itself or one of its remotes
(ordinal comparison of the full names). A null caller party (unknown or not applicable) finds nothing.

## Requires.

- `CK.AppIdentity.Abstractions`: `CK.AppIdentity.Configuration` and `Microsoft.Extensions.Hosting.Abstractions` arrive
  through it.
- `CK.StObj.Model`: the `PartyComponentOptionDirectory` is implemented by the engine (CK.AppIdentity.Engine).
