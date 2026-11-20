# CK-AppIdentity

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-AppIdentity.svg)](LICENSE)

A minimal model of an application and its peers, extensible through "features" attached to the identity
objects, designed to minimize the configuration mess.

| Package | Description | Latest stable |
| [CK.Testing.AppIdentity](CK.Testing.AppIdentity/README.md) | Assembles an `ApplicationIdentityService` without the engine, for the tests of the run time: its `PartyComponentDirectory` is computed by the engine's own analysis. | [![nuget](https://img.shields.io/nuget/v/CK.Testing.AppIdentity.svg?label=CK.Testing.AppIdentity)](https://www.nuget.org/packages/CK.Testing.AppIdentity/) |
|---------|-------------|---------------|
| [CK.AppIdentity.Configuration](CK.AppIdentity.Configuration/README.md) | What you write in `appsettings.json`, and the immutable objects it is analyzed into. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Configuration.svg?label=CK.AppIdentity.Configuration)](https://www.nuget.org/packages/CK.AppIdentity.Configuration/) |
| [CK.AppIdentity.Abstractions](CK.AppIdentity.Abstractions/README.md) | The running model: `IParty` and its four specializations, and the `IFileStore` every party gets. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Abstractions.svg?label=CK.AppIdentity.Abstractions)](https://www.nuget.org/packages/CK.AppIdentity.Abstractions/) |
| [CK.AppIdentity](CK.AppIdentity/README.md) | The implementation: the hosted `ApplicationIdentityService`, the party lifetime, and the feature drivers. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.svg?label=CK.AppIdentity)](https://www.nuget.org/packages/CK.AppIdentity/) |
| [CK.AppIdentity.Hosting](CK.AppIdentity.Hosting/README.md) | One extension method that reads the `"CK-AppIdentity"` section and initializes `CoreApplicationIdentity`. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Hosting.svg?label=CK.AppIdentity.Hosting)](https://www.nuget.org/packages/CK.AppIdentity.Hosting/) |
| [CK.AppIdentity.Engine](CK.AppIdentity.Engine/README.md) | The engine: generates the ordered options of the party components (`PartyComponentOptionDirectory`). | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Engine.svg?label=CK.AppIdentity.Engine)](https://www.nuget.org/packages/CK.AppIdentity.Engine/) |
| [CK.AppIdentity.PartyClient](CK.AppIdentity.PartyClient/README.md) | The "PartyClient" feature: the clients of a party, built by packages and declared in its configuration. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.PartyClient.svg?label=CK.AppIdentity.PartyClient)](https://www.nuget.org/packages/CK.AppIdentity.PartyClient/) |

## Testing on Unix

The store permissions and the inter-process locks behave differently on Unix. From Windows,
`.\Tests\Unix\Run-UnixTests.ps1` runs the tests in a dedicated WSL distribution (the Unix tools of the Stack);
on Linux or macOS, the Stack's `Tools/Unix/run-tests.sh`. See [Tests/Unix](Tests/Unix/README.md).
