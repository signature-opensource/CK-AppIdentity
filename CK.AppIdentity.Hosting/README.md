# CK.AppIdentity.Hosting

One extension method. It reads the `"CK-AppIdentity"` configuration section, registers the resulting
configuration as a singleton, and initializes `CoreApplicationIdentity` from it.

```csharp
public static T AddApplicationIdentityServiceConfiguration<T>( this T builder,
                                                               string? contextDescriptor = null )
    where T : IHostApplicationBuilder
```

Note the constraint: it is `IHostApplicationBuilder`, so it works for a web application, a worker and a
console alike. It is not on `IHostBuilder`.

## What it actually does.

The work is deferred, not done at call time: the method registers a callback through `AddAutoConfigure`,
and that callback runs later, once the configuration is complete. It then

1. builds an `ApplicationIdentityServiceConfiguration` from `builder.Configuration.GetSection( "CK-AppIdentity" )`
   and the host environment,
2. copies the triplet into `CoreApplicationIdentity` - `DomainName`, `PartyName`, `EnvironmentName`,
   plus the `ContextDescriptor` - and initializes it,
3. registers the configuration as a singleton.

If the section yields nothing, none of the three happens: no configuration is registered, and
`CoreApplicationIdentity` stays untouched. That is the whole "application identity is the only aspect
that requires configuration" rule, enforced by absence.

## Two behaviours worth knowing before you call it.

**It is idempotent, and the last `contextDescriptor` wins.** Calling it twice is supported: the second
call does not register a second callback, it only replaces the descriptor - and logs an `Info` naming
both values when they differ. Once `CoreApplicationIdentity` is initialized the replacement is skipped
entirely, since it could no longer have an effect.

**Losing the race is a warning, not a failure.** The identity is configured through
`CoreApplicationIdentity.TryConfigure`, so if something initialized it earlier, this method logs

> Unable to configure CoreApplicationIdentity since it is already initialized: '...'

and carries on to register the configuration singleton. The application still gets its
`ApplicationIdentityServiceConfiguration`; what it loses is the process-level identity matching it. When
that matters, this call has to come before whatever initialized the identity first.

`contextDescriptor` defaults to `Environment.CommandLine`, which is a deliberate choice rather than a
placeholder: it is the one piece of context that distinguishes two processes of the same party.

## Getting the identity service itself.

This method registers the *configuration*. The `ApplicationIdentityService` arrives on its own, because
it is declared `ISingletonAutoService` and the StObj map carries it. So in an application the call
below is all there is:

```csharp
builder.AddApplicationIdentityServiceConfiguration();

var app = builder.CKBuild( map );
```

Two details in there are load-bearing. `CKBuild` rather than `Build`, because it is what runs the
`AddAutoConfigure` callback this method registered - without it the identity is never configured. And
`CKBuild( map )` **with the map**: the map is what makes the auto-service discovery happen, and
therefore what registers the identity service and hooks its hosted lifecycle. Both come from
`CK.Monitoring.Hosting`.

Once the host is built, `IApplicationIdentityService` resolves like any other singleton, and
`InitializationTask` is what to await if you need the features ready.

### Without a map, you register it yourself.

Some tests build a host through `CKBuild()` with no map. There is then no discovery, and the service
needs two lines rather than one:

```csharp
builder.Services.AddSingleton<ApplicationIdentityService>();
builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
```

**The second line is the one to get right.** The service is both what you resolve and the hosted
service that initializes it, so both registrations have to yield the *same* instance - hence the
factory. Writing `AddSingleton<IHostedService, ApplicationIdentityService>()` instead builds a second
instance: the hosted one initializes, the one you resolve never leaves its initial state, and nothing
reports it anywhere.

With no host at all, the service takes its configuration directly and starts through the
`IHostedService` cast:

```csharp
var c = ApplicationIdentityServiceConfiguration.CreateEmpty();
var identity = new ApplicationIdentityService( c, new SimpleServiceContainer() );
await ((IHostedService)identity).StartAsync( default );
await identity.InitializationTask;
```

Both fallbacks come from [`DomainTests`](../Tests/CK.AppIdentity.Tests/DomainTests.cs).

## The "#Dev" folder of a development solution.

The `#Dev` folder holds the development data of a solution, at the root of its git working folder. It is automatic:
nothing to commit, nothing to configure.
- **The build of each party creates it** (with a `.gitignore` that is `*`: git ignores its content), writes the party's
  manifest `#Dev/Parties/<Project>.json` (its project folder, its executable) and copies the generated scripts in `#Dev`
  (`Start.ps1`). They are generated: don't edit them. This package ships these MSBuild targets (`buildTransitive`): they
  reach every project that references it, directly or transitively.
- **A party in Development uses `#Dev/Store`** when it doesn't configure its `StoreRootPath` (see
  [CK.AppIdentity.Configuration](../CK.AppIdentity.Configuration/README.md)): its identity keys, its pinned remotes, its
  running status. Deleting `#Dev/Store` resets the identities of the parties.

The git working folder is the closest folder above the project with a `.git` folder (a main checkout) or a `.git` file (a
linked worktree, a submodule): the same rule as `LocalDevSolution.SolutionFolder` (CK.ActivityMonitor) at run time. Each
worktree has its own `#Dev`. A build outside a git working folder (a downloaded source archive) does nothing, and
`<CKAppIdentityDevFolder>false</CKAppIdentityDevFolder>` opts a project out.

**A project is a party** when it is an executable (`OutputType` `Exe`), is not a test project, and has an
`appsettings.json`. In an App/Builder/Host solution, that is the host: the `.App` is a library and the Builder has no
`appsettings.json`. The optional `<CKAppIdentityParty>true|false</CKAppIdentityParty>` overrides the rule.

**`#Dev/Start.ps1`** starts all the parties at once, in their own window, in the Development environment
(`DOTNET_ENVIRONMENT`), and waits until they are ready:
- **The readiness:** each party's `DevRunningStatus.json` (written by the "DevRunningStatus" feature of
  [CK.AppIdentity](../CK.AppIdentity/README.md) in the party's folder of `#Dev/Store`) says `Ready`. Then its client
  applications are served.
- **Then it prints their URLs, and opens the browser** on the default client application.
- **Enter stops the parties like Ctrl+C** in their window: a hidden helper attaches to their console. A party that
  doesn't exit in time is killed with its process tree.
- **Unless `-SkipBuild`,** it builds the parties first (the TypeScript integration of the build installs the packages of
  the client applications when needed).
- **`-NoBrowser`** doesn't open the browser; **`-Timeout`** sets the seconds to wait (180 by default).

The parties' status files require the `#Dev` environment and the `#Dev/Store` (no configured `StoreRootPath`).

## Requires.

- `CK.AppIdentity`, which brings the configuration type transitively, and `CK.Monitoring.Hosting` for
  `AddAutoConfigure` and the builder monitor.
