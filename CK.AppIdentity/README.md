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
times; nothing in the repository uses it, it exists for that caller.

`InitializationTask` is the handle for anything that must wait: when it completes successfully, every
configured feature is initialized and available.

**It disposes the graph.** `IAsyncDisposable` tears down the features of every party, in order.

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
| `ILocalParty.RemotesChanged` | a remote added to or destroyed on that party |
| `IApplicationIdentityService.AllPartyChanged` | any owned party anywhere in the graph |
| `IApplicationIdentityService.Heartbeat` | the periodic tick, carrying an `int` |

`SystemClock` is exposed beside them, which is what makes the heartbeat testable: a test substitutes the
clock instead of waiting.

## The features are the point.

The objects above do nothing but be what they are. Everything a party actually *does* - managing the key
store of a remote, exposing a third-party API, carrying a transport - arrives as a feature, and features
are described in [`Features/README.md`](Features/README.md), which also covers the "Package First"
approach and the feature configuration.

[`BasicTrampoline`](Impl/BasicTrampoline/README.md) documents the helper the feature setup uses to run
ordered, possibly-failing steps.

## Requires.

- `CK.AppIdentity.Abstractions`, and nothing else directly: `CK.AppIdentity.Configuration` and
  `Microsoft.Extensions.Hosting.Abstractions` arrive through it.
