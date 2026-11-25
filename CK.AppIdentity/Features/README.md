# The Features

If the ["run time" model](../../CK.AppIdentity.Abstractions/README.md) is a skeleton, the Features are the muscles.

Features are not typed: they are objects that can be very simple or very complex, and which decorate
each Party. From the outside, the API is trivial:

![Features on a Party](../docs/PartyFeature.png)

Features can only be added, not removed. 

## The Package First approach

In practice, we don't use this `AddFeature` method directly, as the addition and initialization of the Features that decorate each Party is
carried out automatically by the Feature drivers, which are automatic singleton services: simply installing the Feature's NuGet package
makes it operational (this is the "Package First" approach). To facilitate development, a Template Method is available, which you simply
need to specialize.

A driver is responsible for:
- Creating, initializing and adding any Features it wishes to the system's initial Parties (by implementing the `SetupAsync` method);
- Doing the same for any dynamic Parties that appear (by implementing the `SetupDynamicRemoteAsync` method);
- doing whatever is necessary when a dynamic Party is deleted (by implementing the `TeardownDynamicRemoteAsync` method);
- and finally, doing whatever is necessary when the entire Service is deleted, which in practice corresponds to a host application
  shutdown (by implementing the `TeardownAsync` method).

These functions are called by the micro agent ([AppIdentityAgent](../AppIdentityAgent.cs)) which manages the lifecycle of Parties and
Features.

![FeatureDriver](../docs/FeatureDriver.png)

A driver declares its feature with its [`PartyFeatureAttribute`](https://github.com/signature-opensource/CK-Abstractions/blob/stable/CK.AppBuilder.Abstractions/Party/PartyFeatureAttribute.cs): its name and its activation,
explicitly (the attribute is not inherited, a driver without it is an error of the engine). A Feature can be opt-in
(`TypeActivation.Optional`: a party has it only when it declares it), opt-out (`TypeActivation.ByDefault`: a party has it
unless it turns it off) or always there (`TypeActivation.Required`).
```csharp
[PartyFeature( "KeyManagement", TypeActivation.ByDefault )]
public sealed class KeyManagementFeatureDriver : ApplicationIdentityFeatureDriver
{
    public KeyManagementFeatureDriver( ApplicationIdentityService s ) : base( s ) { }
}
```
The engine reads the feature names, the activation and the sides of the drivers (`PartyComponentAnalysis`,
CK.AppIdentity.Engine) into the generated `PartyComponentDirectory`: a driver reads them there (no reflection at run time).
A test that assembles its services without the engine gets the same directory from CK.Testing.AppIdentity.

The configuration then takes over: a party **declares** a feature by its key (see [Feature configuration](#feature-configuration)):

- The feature name is the name declared by the driver's `[PartyFeature]` (a `XXXFeatureDriver` usually manages the "*XXX*"
  feature). It is this name that is the key in the configuration.
- The driver implementation can then use the `bool IsAllowedFeature( IParty p )` helper method, which gives it the
  declaration of the party (its own key, then the "Defaults" above it), or the opt-in/opt-out default when there is none.
- Setup methods can fail. Rather than throwing an exception, they are prompted to issue error logs and simply return false (but if they
  throw, this is of course handled).
- Teardown methods are not expected to fail.

The order in which driver methods are called respects the dependencies between drivers: if the driver in Feature "A" depends on
Feature "B", then :

- `SetupAsync` and `SetupDynamicRemoteAsync` of "A" will be called after the setup methods of "B" have been called.
- The Teardowns are reversed: "B" will only be called upon after "A" has done its cleaning job.

## Asymmetric features

An **asymmetric feature** is implemented by one party and used by others, which relay it to that party: an
Authentication Center, or a mail sender that holds the mail server's configuration and its secrets for the parties
that send mails through it.

### The packages

For the asymmetric feature "*XXX*":

| Package | Content |
|---|---|
| CK.AppIdentity.XXX.Local | The core code: the `LocalXXXFeatureDriver`, the implementation of the feature. It references CK.AppIdentity.XXX.Remote: the Local side requires the Remote side. |
| CK.IO.XXX | The Cris commands (public or internal) that the remotes send to the local party that is the feature. |
| CK.AppIdentity.XXX.Remote | The `RemoteXXXFeatureDriver` and the code that relays the feature to the remote that is the feature. |
| CK.AppIdentity.XXX | Optional: the common implementation. |

The two drivers declare the feature and their side: `[PartyFeature( "XXX", Side = FeatureSide.Local )]` and
`[PartyFeature( "XXX", Side = FeatureSide.Remote )]` (the prefix is never removed implicitly: a `LocalStorageFeatureDriver`
manages "*LocalStorage*"). Their feature names for the type sets are "*XXX*" and "*XXXRemote*" (the keys that declare
them).

- **The Local side requires the Remote side** (`[Requires<RemoteXXXFeatureDriver>]`): the "*XXXRemote*" key of a remote
  is read by both sides (the Remote side: "relay to it"; the Local side: "it may call me"). In a party that is the
  feature, the Remote driver does nothing.
- **What the two sides share** is first a shared static helper that both call; a common abstract base class (the
  "template method" pattern) is possible when that is not enough. A common driver is never concrete: only the sides
  are packages.
- **The two sides agree on their optionality** (the engine checks it). It is optional by default, and allowed by default
  makes little sense (every local party would be the feature and every remote one of its callers): it must be exceptional.
- A feature name is borne by one driver: two drivers can never manage the same feature otherwise (the engine checks it).

### The configuration

The key is "*XXX*" in the `Local` of a local party (the root or a tenant domain) and "*XXXRemote*" on a remote
(`GetFeatureName( IParty p )` gives the key of a party). The "*XXX*" of the `Local` gives the role of the local party:

| | The local party declares "*XXX*": it **is** the feature | The local party doesn't declare "*XXX*" |
|---|---|---|
| The Local side (`LocalXXXFeatureDriver`) | implements the feature. The remotes that declare "*XXXRemote*" are the ones that can call it (a "Defaults" that declares "*XXXRemote*" lets all of them call it): the calls of a remote that doesn't declare it are refused. | does nothing (as if it was not there). |
| The Remote side (`RemoteXXXFeatureDriver`) | does nothing (as if it was not there). | relays the feature to **the** remote that declares "*XXXRemote*", if any: this remote is the feature. |

- **The feature can be configured on both sides of the relationship:** the `Local:XXX` section configures the feature,
  and a remote can have a "*XXXRemote*" section that configures how the remote is used or considered.
- **A local party uses at most one remote that is the feature:** more than one remote that declares "*XXXRemote*" (when
  the local party doesn't declare "*XXX*") is an error. `TryGetFeatureRemote` gives this remote.
- **The Local and the Remote sides can coexist in the same process:** the "*XXX*" of each local party gives its role
  in its tenant domain.
- **A remote that declares only "*XXX*" is an error** ("*XXX*" is the key of a local party that is the feature): it fails
  the setup of the feature. Declaring both names is not an error (a "Defaults" applies to the `Local` and to the
  remotes). A `Local` can declare "*XXXRemote*": the party has the Remote side, for the remotes added later.
- **A remote added later can be the feature** of a local party that doesn't declare "*XXX*": the Remote side handles it
  like a remote of the initial configuration.
- **The roles are announced:** the [description of a local party](../README.md#the-description-of-a-party) carries the
  asymmetric features it is (`PartyDescription.Roles`). A local party that relays "*XXX*" to a remote checks that the
  remote announces being "*XXX*": the forgotten "*XXX*" of the remote's `Local` is an error when the remote announces
  its description, a warning for its last known description at setup. No setup-time rule can see it: the mistaken
  configuration is a valid one of the Remote side.

`IsAsymmetric`, `HasLocalSide` and `HasRemoteSide` tell a driver which drivers of the feature are registered.

A **singleton asymmetric feature** exists only once in a tenant: there is one Authentication Center in a tenant,
whereas there can be several mail senders. This is a documented variation of the pattern: nothing checks it.

## Static features

A **static** feature is allowed only on the parties of the initial configuration: the root, the tenant domains and the
remotes that are not `IOwnedParty.IsDynamic`. Its lifetime is the host's. Its driver specializes
[`ApplicationIdentityStaticFeatureDriver`](ApplicationIdentityStaticFeatureDriver.cs) instead of
`ApplicationIdentityFeatureDriver` (same constructor, same `[PartyFeature]`).

- **Only the parties that ARE the feature are constrained:** the remotes that use an asymmetric feature can be dynamic
  (a remote is the feature when its local party is not).
  - **A dynamic party that explicitly allows a static feature fails its setup.** Dynamic remotes are still
    supported: they just cannot have a static feature.
  - **When the feature is allowed by default, the default doesn't apply to the dynamic parties:** `IsAllowedFeature`
    returns false for them.
- **The dynamic remotes of the feature** go through `SetupDynamicFeatureRemoteAsync` and
  `TeardownDynamicFeatureRemoteAsync` (by default, nothing to do). `SetupDynamicRemoteAsync` and
  `TeardownDynamicRemoteAsync` are sealed: they check the rule and call them.
- **Two host hooks bracket its lifetime, outside the agent's loop:**

  | Hook | Called | Order |
  |---|---|---|
  | `OnHostStartedAsync` | once the initialization has succeeded (all the features are set up and published): the hosted service's `StartedAsync`, or `StartAndInitializeAsync` | the drivers' order |
  | `OnHostStoppingAsync` | before the agent stops (before any teardown): the hosted service's `StoppingAsync`, or `DisposeAsync`. Only if `OnHostStartedAsync` succeeded | reverse order |

  This is where a driver opens and closes what makes the party reachable from outside, an HTTP endpoint for instance:
  the doors open once the party is whole and close while it still is, so the requests in progress can complete.
  - **A failing `OnHostStartedAsync`** fails the start: the drivers already started are stopped.
  - **Each hook runs once.** A stop cancels a start that still waits for the initialization.
  - **Because they run outside the agent's loop,** a hook can wait for an agent job (adding a party, for instance).
  - **A code that calls only `IHostedService.StartAsync`** (and awaits `InitializationTask`) doesn't call the hooks:
    `StartAndInitializeAsync` does.

## Feature configuration

A party (a `Local` section, a remote) **declares a feature by its key**: `"AuthCenter": true`, or the section of the
feature (`"HttpEndpoint": { ... }`); `"KeyManagement": false` turns off a feature that is allowed by default. The
declaration is the party's own key, otherwise the one of the nearest "Defaults" above it. Without declaration, the
activation of the FeatureDriver (described above) applies.

The declarations follow the activation, checked at start (and when a dynamic party is added):

| Activation | No key | `true` | `false` | A section |
|---|---|---|---|---|
| `Optional` | off | on | off, a warning (redundant) | on, configured |
| `ByDefault` | on | on, a warning (redundant) | off | on, configured |
| `Required` | on | on, a warning (redundant) | **an error**: the party doesn't start | on, configured |

**The keys of a party are a closed set:** its own keys (`PublicUrl`, `Assemblies` and `DefaultAssembly` in a `Local`;
the identity, `Address`, `Assemblies` and `DefaultAssembly` on a remote) or features. The settings of a feature are in
its section (`"KeyManagement": { "KeyRotationDays": 30 }`), and what a package brings to a feature is an option of the
feature, in the same section.

The names are case insensitive (like the full names of the parties). A key that is neither a key of the party nor the
name of a registered feature (its `FeatureName`, or `FeatureName` + "Remote" for an asymmetric feature) is reported when
the agent starts, and when a dynamic party is added: a warning, an error in `StrictConfigurationMode` (the start or the
addition fails). A typo in `"KeyManagment": false` would otherwise silently leave a feature allowed by default on, and
a setting misplaced at the root of a party would be ignored.

Thanks to the topological ordering of the FeatureDrivers (described above), a Driver can decide whether a Feature is available
depending on the status of another Feature (absence, presence, or even some of its configurations). 

For an example in packages built on this one, see the
[CK-AppIdentity-Transport](https://github.com/signature-opensource/CK-AppIdentity-Transport/tree/stable) repository: there, the "Transport"
feature (`TransportFeatureDriver`, in CK.AppIdentity.TransportLayer) requires the "KeyManagement" feature (the `IRemoteKeys` of a
remote, in CK.AppIdentity.KeyManagement) to be available on a party: if this is not the case, the `TransportFeatureDriver` issues a warning:
*"Remote 'AcmeCorp/$Server' cannot support the allowed 'Transport' feature because the remote has a disallowed 'KeyManagement' feature."*.

A driver reads the configuration of a party: the `LocalConfiguration` of a local party (the root or a tenant domain, its "Local"
section) or the configuration of a remote. Nothing is implicitly inherited from the levels above: a setting that can have a default
value is resolved with the `TryResolveValue` / `TryResolveSection` extension methods of CK.Configuration (the party's own key, then the
"Defaults" sections above it, see [the levels and the defaults](../../CK.AppIdentity.Configuration/README.md#the-structural-levels-and-the-defaults)).
A setting that must be configured on each party (anything to do with security, for example) is read on the party only
(`TryGetSection`).

### A feature with a configuration model

A feature driver may declare the configuration model of its feature (`[Configuration<TModel>]`, CK.ConfigurationModel):
then it doesn't read its section, it gets the model of each party, `ApplicationIdentityFeatureDriver.GetModel<TModel>( party )`.
The model is read before the driver sets the party up (with its options), from the feature's section **key by key along
the levels** (the party's section, then the one of each "Defaults" above it: `ApplicationIdentityConfiguration.GetFeatureLevels`),
the names of the options of the feature reserved (an option is a key of its owner's section). The model is closed: an
unknown key, a missing required one or an invalid value is an error (all of them are logged) and the setup fails; a
party without section gets the defaults of the model. The model of an asymmetric feature is the one of its side: the
Local side has the model of the local parties ("XXX"), the Remote side the one of the remotes ("XXXRemote"). The engine
generates the description of the model
(`PartyComponentInfo.Model`, CK.ConfigurationModel.Engine): no reflection at run time; the model must be public.

__Important:__ There is no namespace or other partitioning of names/keys: drivers who don't know each other can perfectly well use
common keys... or should but don't. We didn't consider this to be an issue at this stage, and even saw it as a
strength of the system: the same configuration key can therefore impact several Features "naturally". 

## The ServiceLocator anti-pattern?

Remotes carry the object-features. To find them, you have to go looking for them.
This is definitely closer to [Service Locator](https://en.wikipedia.org/wiki/Service_locator_pattern) than to
dependency injection. But how else can you do it? One possibility is to use "indirection keys" in the
DI container. All too often, these "contexts" or other "ServiceKeys" lead to a nightmare in terms of
maintainability. We don't want to use them, we've banned them from our designs because they lead to
far too much accidental complexity.

To solve the problem elegantly, all you have to do is take the time to create services (singletons or scoped)
that will manage a Feature "centrally". This is simply a rewriting of the API. 

Let's take the case of a Feature `F` that exposes a `DoSomething()` method. The developer has a
Party `p` and wishes to call `DoSomething`:
```csharp
p.GetRequiredFeature<F>().DoSomething();
```
The `Get(Required)Feature` call here is the Service Locator. Now let's create a very simple service:

```csharp
public class FService : IAutoService
{
    public void DoSomething( IParty p ) => p.GetRequiredFeature<F>().DoSomething();
}
```

The developer can now inject the `FService` where it needs to and call `DoSomething()` when he needs
it.

This type of "**Feature as a Service**" can go one step further. For example, the "Server" Party for
an Agent that is intrinsically connected to its server. In this case, we can model/express this
wellknown Party as a service:

```csharp
public class ServerService : IAutoService
{
    readonly IRemoteParty _serverParty;
    readonly FService _fService;

    public ServerService( IApplicationIdentityService appIdentity, FService fService )
    {
        _serverParty = appIdentity.AllRemotes.Single( p => p.PartyName == "$Server" );
        _fService = fService;
    }

    public void DoSomething() => _fService.DoSomething( _serverParty );
}
```

The feature is looked up when `DoSomething` is called, not in the constructor: features are set up
asynchronously, and are available once `IApplicationIdentityService.InitializationTask` has completed.

Please note that the `ServerService` is an `IAutoService`: it will automatically be singletonized
if it can be. In this case, it can because its dependencies are singletons (`IApplicationIdentityService`
is an `ISingletonAutoService`), but if one of them were a scoped service, it would automatically be scoped.

