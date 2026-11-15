# CK.Testing.AppIdentity

Assembles an `ApplicationIdentityService` without the engine, for the tests of the run time of CK.AppIdentity and of the
packages that bring feature drivers.

In an application, the engine generates the `PartyComponentDirectory`: the party components, their requirements, their
abstractions, their order, their feature names, their optionality and the sides of the asymmetric features (the
feature drivers read their name and their optionality in it). A test that assembles its services by hand gets the same
directory from `AppIdentityTestHelper`: it is computed from the component types by `PartyComponentAnalysis` (CK.AppIdentity.Engine),
the code that the generated directory is written from. There is no other path: a service without directory is an error.

| Method | Use |
|--------|-----|
| `IServiceCollection.AddApplicationIdentityForTests( configuration, drivers, otherComponents )` | Registers the configuration, the `ApplicationIdentityService` (and its `IHostedService`), the feature drivers in their order and the directory of the drivers and of the other components (the client features...). |
| `CreateComponentDirectory( types )` | The directory alone. |
| `CreateServiceProvider( types )` | A service provider with the directory alone: for a test that creates its `ApplicationIdentityService` and its drivers itself. |

The tests that run the engine (a compiled map) have the generated directory: they don't need this package.
