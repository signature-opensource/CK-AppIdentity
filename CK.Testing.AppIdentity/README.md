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
| `IServiceCollection.AddPartyComponentDirectory( otherComponents )` | The directory of the feature drivers already registered in the services, and of other components. |
| `CreateComponentDirectory( types )` | The directory alone. |
| `CreateServiceProvider( types )` | A service provider with the directory alone: for a test that creates its `ApplicationIdentityService` and its drivers itself. |
| `IServiceCollection.AddPartyComponentOptions( optionTypes )` | A `PartyComponentOptionDirectory` of option types, simpler than the generated one: the options of an owner are in the given order (no requirements) and apply to their declared owner type only; they are created by reflection (their `IConfiguration` parameter is their configuration). |

The tests that run the engine (a compiled map) have the generated directories: they don't need this package. A test that
lists the types of its map lists them too: `typeof( PartyComponentDirectory )`, and `typeof( PartyComponentOptionDirectory )`
when options are used.
