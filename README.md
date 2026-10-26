# CK-AppIdentity

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-AppIdentity.svg)](LICENSE)

A minimal model of an application and its peers, extensible through "features" attached to the identity
objects, designed to minimize the configuration mess.

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.AppIdentity.Configuration](CK.AppIdentity.Configuration/README.md) | What you write in `appsettings.json`, and the immutable objects it is analyzed into. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Configuration.svg?label=CK.AppIdentity.Configuration)](https://www.nuget.org/packages/CK.AppIdentity.Configuration/) |
| [CK.AppIdentity.Abstractions](CK.AppIdentity.Abstractions/README.md) | The running model: `IParty` and its four specializations, and the `IFileStore` every party gets. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Abstractions.svg?label=CK.AppIdentity.Abstractions)](https://www.nuget.org/packages/CK.AppIdentity.Abstractions/) |
| [CK.AppIdentity](CK.AppIdentity/README.md) | The implementation: the hosted `ApplicationIdentityService`, the party lifetime, and the feature drivers. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.svg?label=CK.AppIdentity)](https://www.nuget.org/packages/CK.AppIdentity/) |
| [CK.AppIdentity.Hosting](CK.AppIdentity.Hosting/README.md) | One extension method that reads the `"CK-AppIdentity"` section and initializes `CoreApplicationIdentity`. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Hosting.svg?label=CK.AppIdentity.Hosting)](https://www.nuget.org/packages/CK.AppIdentity.Hosting/) |

## Testing on Unix

The store permissions and the inter-process locks behave differently on Unix. From Windows,
`.\Tests\Unix\Run-UnixTests.ps1` runs the tests in a dedicated WSL distribution that it creates
and provisions itself; on Linux or macOS, run `Tests/Unix/run-tests.sh`.
See [Tests/Unix](Tests/Unix/README.md).
