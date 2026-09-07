# CK-AppIdentity

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-AppIdentity.svg)](LICENSE)

A minimal model of an application and its peers, extensible through "features" attached to the identity
objects, designed to minimize the configuration mess.

Application identity may be the only aspect that requires an explicit configuration. Any other aspect
can have a default behavior, but the remote parties an application interacts with, and how it interacts
with them, can hardly exist without configuration.

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.AppIdentity.Configuration](CK.AppIdentity.Configuration/README.md) | What you write in `appsettings.json`, and the immutable objects it is analyzed into. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Configuration.svg?label=CK.AppIdentity.Configuration)](https://www.nuget.org/packages/CK.AppIdentity.Configuration/) |
| [CK.AppIdentity.Abstractions](CK.AppIdentity.Abstractions/README.md) | The running model: `IParty` and its four specializations, and the `IFileStore` every party gets. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Abstractions.svg?label=CK.AppIdentity.Abstractions)](https://www.nuget.org/packages/CK.AppIdentity.Abstractions/) |
| [CK.AppIdentity](CK.AppIdentity/README.md) | The implementation: the hosted `ApplicationIdentityService`, the party lifetime, and the feature drivers. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.svg?label=CK.AppIdentity)](https://www.nuget.org/packages/CK.AppIdentity/) |
| [CK.AppIdentity.Hosting](CK.AppIdentity.Hosting/README.md) | One extension method that reads the `"CK-AppIdentity"` section and initializes `CoreApplicationIdentity`. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Hosting.svg?label=CK.AppIdentity.Hosting)](https://www.nuget.org/packages/CK.AppIdentity.Hosting/) |

Read them in that order, which is also the dependency order: `CK.AppIdentity.Abstractions` references
`CK.AppIdentity.Configuration`, not the reverse. The configuration comes first because the model is
built from it and nothing else - an identity object exists because a configuration section described
it, or because it was added dynamically at runtime through the same configuration shape.

The initial objects are locked once the application starts and cannot change during its lifetime. New
objects can be defined dynamically and destroyed, but what was configured is immutable.
[CK.Configuration](https://github.com/signature-opensource/CK-Configuration/blob/master/CK.Configuration/README.md)
provides the `ImmutableConfigurationSection` that guarantees it.
