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

A configured party is immutable and cannot be destroyed. A dynamic remote is added to a local party (the root
or a tenant domain) through the same configuration shape, as a `MutableConfigurationSection` built in code:

```csharp
Task<IReadOnlyCollection<IRemoteParty>?> AddMultipleRemotesAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration );
Task<IRemoteParty?> AddRemoteAsync( IActivityMonitor monitor, Action<MutableConfigurationSection> configuration );
```

Both return null on failure rather than throwing - the monitor carries the reason. Adding several remotes is all or
nothing: if one fails to be set up, none is added.

The tenant domains are configured (the `Parties` of the root that have a domain), or they are the run-time instances of
an Opened tenant template (see below): no public API adds a tenant domain from a free configuration.

Destruction is `IOwnedParty.DestroyAsync`, and `SetDestroyed` is its two-phase companion: it marks the
party so that no one else starts working with it, and returns whether this call is the one that won the
race. The folder of a destroyed party (and of the remotes of a destroyed tenant domain) is deleted when it holds no file:
a candidate that has never been approved leaves no debris. A folder with a file is kept (a pin, a description, the
`-Local` of a party that runs here or in another application that shares the store), and a file written at the root of
a party's store creates its folder again: another application of the store may still have a party of this name.

### One process per local party

A local party (the root, a tenant domain) holds a lock on its private store (`-Local/$Lock.Party`) while it runs: two
processes of the same party would share its keys and its records. The root and the configured tenant domains are locked
at start, before the setup of the features: a party that runs in another process fails the start (the same `--Instance`
of a fleet started twice, the same application started twice on the same store). A dynamic tenant domain is locked
before its setup: one that runs in another process is not created. The lock is released when the party is shut down or
destroyed, and when the start fails (a failed start runs nothing). Several processes of the same party on the same store
(the scaling out) are therefore refused; separate stores are not concerned.

### The dynamic instances of an Opened template

An Opened remote template (`"MaxInstanceCount"`, see CK.AppIdentity.Configuration) accepts dynamic instances, numbered
after its configured ones up to its bound: `ILocalParty.RemoteTemplates` lists them.

- **A candidate:** `ILocalParty.TryAddTemplateInstanceAsync` creates the dynamic remote that a full name names
  (`Sample/$Worker-3/#Dev`) when it is an instance of a template within its bound and not already a remote. The
  transport calls it for an unknown caller: the remote is a candidate until its identity is pinned (by an approval).
- **Nothing else is stored:** the store is a trust store, a dynamic instance exists because its identity is pinned in its
  folder (`IFileStore.TrustFilePattern`). At start, the agent creates the dynamic instances whose folder holds a pin; an
  instance above the bound (the configuration lowered it: the newer decision) has its pins trashed, with a warning.
- **The removal:** `ILocalParty.RemoveTemplateInstanceAsync` destroys the remote and trashes its pins, never its folder (in
  a shared store, it is also the instance's own folder, with its `-Local`). The instance can then be approved again.

### The run-time tenant domains

An Opened tenant template (`IApplicationIdentityService.TenantTemplates`, see CK.AppIdentity.Configuration) holds tenant
domains created by the application: an operator, or the application itself (a sign-up that creates a customer tenant).

- **The creation:** `AddTenantInstanceAsync( monitor, template, "Acme" )` creates the tenant domain
  `{DomainPrefix}/Acme/$Acme`: its index is the first free one (no configured instance, no loaded tenant has it, up to
  the `"MaxInstanceCount"`), computed in the agent's loop (two creations never take the same index). The tenant domain is
  set up (its features, its keys, its remotes, its listeners), then its record is written, last: a failure leaves no
  record.
- **The record:** the index can't be derived from a free name, so it is stored in the private store of the tenant:
  `-Local/Instance.json` (`{ "Index": 3 }`). A run-time tenant is a folder under the domain of its template that holds
  this file; nothing else is stored. At start, the agent creates them again (before the pinned remote instances: the
  Opened remote templates of a tenant find theirs).
- **The unloaded tenants:** a record that the configuration contradicts (its index taken by a configured instance, or
  above a lowered bound), or a tenant that can't be created (it runs in another process...), is not loaded. It is kept,
  never trashed (its private store holds its keys), and listed with its reason (`UnloadedTenants`). A record whose name
  is now a configured instance is dropped (the configured tenant domain is the same party).
- **The move:** `SetTenantIndexAsync( monitor, fullName, index )` moves a run-time tenant (a loaded or an unloaded one) to
  a free index: its identity doesn't change (its full name, its keys), its record does, and the values that use its index
  (`{Tenant.Index}`): a loaded tenant domain is destroyed and created again. It also makes room for a configured
  instance.
- **The removal:** `RemoveTenantInstanceAsync( monitor, fullName )` destroys it, trashes the pins of its run-time remote
  instances (their folders are under its domain: a new tenant of the same name would bring them back), lets the feature
  drivers delete what must not survive in a trash bin (`ApplicationIdentityFeatureDriver.ForgetLocalPartyAsync`: the
  private keys) and trashes the rest of its private store, its record with it. Its identity is gone: a new tenant of the
  same name is a new identity.

The tenants of an application share its listeners (a peer reaches a tenant at the address of the application: the
transport selects the tenant by the caller). One peer can't be a remote of two local parties of the same application
(a known limit: a remote is found by the full name of the caller).

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
Otherwise it is dormant: it checks once and does nothing more. `"DevRunningStatus": false` switches it off.
- **The file is written** when the host has started (`OnHostStartedAsync`), rewritten after a change (batched over
  200 ms), and deleted when the host stops. The write is atomic: a temporary file, then a rename.
- **Its core properties:** `FullName`, `ProcessId`, `ProcessStartTime` (with the pid, they detect a stale file),
  `ContentRootPath`, `Ready` and the `Remotes` (`FullName`, `Outgoing`).
- **The packages contribute their own properties** with an `IDevRunningStatusContributor` (CK.AppIdentity.Abstractions):
  for instance, the transport (the remotes' availability), the HTTP endpoint, the client applications. `Ready` is
  true when every contributor is ready. A section can hold a `Pending` array: what an operator must do for the party
  (an approval, for instance), printed by the launcher; what waits for an operator doesn't make a party not ready. A feature driver implements it itself (explicitly: it doesn't add to its
  public API), as the transport and the HTTP endpoint do; a separate contributor is for a package that has state of
  its own and no driver (the client applications).
- **A contributor stays dormant until the writer activates it:** it subscribes to nothing and computes nothing before.

The launcher of the solution, `#Dev/Start.ps1` (see [CK.AppIdentity.Hosting](../CK.AppIdentity.Hosting/README.md)),
reads these files.

## The description of a party.

A party states what it is to the others: its public URL, its client applications, its roles and its encryption key (`IParty.Description`,
a `PartyDescription` of CK.AppIdentity.Abstractions). The AuthCenter, for instance, builds the links of its mails with the
URL of the party through which the user acts, and derives the origins it accepts from the URLs of its remotes.

The description is intrinsic to the parties, not a feature: the `ApplicationIdentityService` sets it
([`PartyDescriber`](PartyDescription/PartyDescriber.cs)):
- **A local party's public URL is its `Local:PublicUrl`** configuration: an absolute `http` or `https` URL without path,
  query nor fragment (`PartyDescription.TryParsePublicUrl`); an invalid one fails the start.
- **The feature drivers complete the description of every local party** (the root and the tenant domains alike): a
  driver overrides `ApplicationIdentityFeatureDriver.ContributeDescription( party, builder )`. The HTTP endpoint gives
  the public URL of the root party when `Local:PublicUrl` is not configured, the "PartyClient" feature gives the client
  applications of each party. It is computed once all the drivers have set up the party, in their order, then each
  time a driver signals a change (`OnDescriptionChangedAsync`): `ILocalParty.DescriptionChanged` is raised when it
  changed.
- **A driver states facts** (`ContributeDescription`, a `PartyDescription.Builder`): the public URL, the client
  applications (and their facts), the data announcements (`SetData( name, value )`). A description is what a party
  states about itself to its remotes: nothing in it is forwarded (a System is a star around its Authentication Center:
  what the System needs to know, the center reads from the descriptions of its remotes and states it).
- **Data announcements are an extensible part of the description.** `PartyDescription.Data` holds named strings
  (announced, compared, persisted).
- **The encryption key of the party** (`PartyDescription.EncryptionKey`, a [`PartyEncryptionKey`](../CK.AppIdentity.Abstractions/PartyEncryptionKey.cs)):
  its public key and the signature of this public key by the party's identity key, set by the key management
  (`PartyDescription.Builder.EncryptionKey`). Only bytes here: its verification and its use belong to the key
  management. Short-lived, it is announced and compared, never persisted.
- **`ILocalParty.RemoteDescriptionChanged`** is raised when a remote announces another description (by the agent's
  loop: `IRemoteParty.SetDescription` stays synchronous). The last known description read at set up raises nothing.
- **The roles of a local party are the [asymmetric features](Features/README.md#asymmetric-features) it is** (their
  Local side is installed and its `Local` allows them). A local party that relays an asymmetric feature to a remote
  checks that the remote announces it. No other feature name is announced: the description is what the other parties
  act on.
- **A remote's description is its last announcement** (`IRemoteParty.SetDescription`, called by the channel), persisted
  in the remote's folder of the shared store (`PartyDescription.json`: it is the remote's own statement, the same for
  every local party) and read back when the remote is set up, so that it is known while the remote is not connected. The
  file is written only when its persisted part changes (not for the encryption key).
  A change of its public URL is logged as a warning.

The URL of a client application is derived, never stored: `PartyDescription.GetClientApplicationUrl( name )` (the
default one, served at `/`, when the name is null).

**The caller party** of a call (the `CallerPartyFullName` of its authentication information) is found with
`CallerPartyExtensions.FindCallerParty` on the local party that received it: the local party itself or one of its remotes
(ordinal comparison of the full names). A null caller party (unknown or not applicable) finds nothing.

## Requires.

- `CK.AppIdentity.Abstractions`: `CK.AppIdentity.Configuration` and `Microsoft.Extensions.Hosting.Abstractions` arrive
  through it.
- `CK.StObj.Model`: the `PartyComponentOptionDirectory` is implemented by the engine (CK.AppIdentity.Engine).
