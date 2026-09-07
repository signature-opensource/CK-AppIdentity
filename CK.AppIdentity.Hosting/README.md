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

## Requires.

- `CK.AppIdentity`, which brings the configuration type transitively, and `CK.Monitoring.Hosting` for
  `AddAutoConfigure` and the builder monitor.
