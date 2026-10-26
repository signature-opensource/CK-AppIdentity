# Application identity model configuration

The goal of this configuration is to be the single central point of configuration for an application (called a Party)
and its peers (its Parties) and to "carry" all the communication and interaction services with its parties:
- It is as simple as possible to configure.
- Once initially configured, no configuration change is possible during the execution **for what has been configured**.
- Dynamic parties are supported (these are the non initially configured beasts).

Application identity may be the only aspect that requires an explicit configuration. Any other aspect
can have a default behavior, but the remote parties an application interacts with, and how it interacts
with them, can hardly exist without configuration.

> One should be able to understand the role of an application and its interactions with the external world
by looking at its configuration.

The [configuration samples](docs/Samples.md) introduce the basic concepts of CK.AppIdentity step by step: remote
parties, which side is the client and which is the server, tenant domains, the "Local" sections and the inheritance
of the configuration keys.

## Configuration keys

| Key | Where | Description |
|-----|-------|-------------|
| `FullName` | root, parties | `"Domain/$Party/#Environment"`. The party and the environment can be omitted. |
| `DomainName`, `PartyName`, `EnvironmentName` | root, parties | The 3 parts of the full name, when `FullName` doesn't define them (`DomainName` cannot be used with `FullName`). A party inherits the domain and environment names of its parent. |
| `Parties` | root, tenant domains, groups | The parties. An item with no `PartyName` (nor `FullName`) is a group: its `Parties` are flattened into the enclosing list. An item with no `Address` whose `PartyName` is the last segment of its domain (`"AcmeCorp/$AcmeCorp"`) is a tenant domain; any other item is a remote party. |
| `Address` | remote parties | Where the remote can be reached. The party that declares the address initiates the communication (the client); the other side listens (the server). |
| `Local` | root, tenant domains | The configuration that applies to the local application (or to the tenant), see the [samples](docs/Samples.md#the-local-configurations). |
| `AllowFeatures`, `DisallowFeatures` | everywhere | Feature names: a string, a comma-separated string or an array of strings. Inherited downward. |
| `DefaultAssembly`, `Assemblies` | everywhere | The [`AssemblyConfiguration`](https://github.com/signature-opensource/CK-Configuration/blob/stable/CK.Configuration/README.md#assemblyconfiguration) of CK.Configuration: the "plugins/dynamic extension" assemblies. Inherited downward. |
| `StrictConfigurationMode` | root | Whether warnings are errors (see [below](#strictly-checking-the-configuration-or-not)). |
| `StoreRootPath` | root | A fully qualified path. When not configured, the store is private and defaults to `ApplicationIdentityServiceConfiguration.DefaultStoreRootPath` (the `CK-AppIdentity` folder in `Environment.SpecialFolder.LocalApplicationData`). |
| `TrashBinRetention` | root | How long trashed files are kept: a `TimeSpan` like `"7.00:00:00"` (the default, 7 days). |

The DomainName, PartyName and EnvironmentName are strictly defined
(see [CK.Core.CoreApplicationIdentity](https://github.com/signature-opensource/CK-Core/tree/stable/CK.Core/CoreApplicationIdentity)).

The FullName identifies a Party: it must be unique **regardless of the case**. "AcmeCorp/$Trolley1/#Production" and
"acmecorp/$trolley1/#production" denote the same Party, so a configuration that defines both is invalid, and a dynamic
party whose name differs only by case from an existing one is rejected. This is stricter than CoreApplicationIdentity
(which only states that the DomainName is case sensitive) and avoids clashes between the Parties' file stores on
case insensitive file systems. The file stores belong to the running model, which is built on this package: for their
layout, see [The FileStore](../CK.AppIdentity.Abstractions/README.md#the-filestore) in CK.AppIdentity.Abstractions.

The configuration is provided by the .NET abstraction
[IConfigurationSection](https://learn.microsoft.com/fr-fr/dotnet/api/microsoft.extensions.configuration.iconfigurationsection)
that can originate from multiple configuration providers (the samples are in JSON).

## Mutable and immutable IConfigurationSection helpers (from CK.Configuration).

To manage this lower level of configuration (and guaranty its immutability), we use [CK.Configuration](https://github.com/signature-opensource/CK-Configuration/blob/stable/CK.Configuration/README.md).

A `ImmutableConfigurationSection` is a `IConfigurationSection` that captures once for all the content and path of any other `IConfigurationSection`.

Each application identity objects is bound and exposes its immutable configuration section.

The `MutableConfigurationSection` acts as a builder for immutable configuration and hence is used to
initialize dynamic parties.


### Strictly checking the configuration (or not)

A `StrictConfigurationMode` boolean property drives whether warnings must be considered as errors.
This defaults to false only when environment name is "#Dev" (the default, non configured environment
name) but can always be explicitly configured at the root configuration level.

The goal is to be strict but some configuration may emit annoying warnings. When this happens,
the global approach is to temporarily set the `StrictConfigurationMode` to false and to consider
those annoying warning as "bugs" that must be fixed by emitting an Info instead of a Warn. Once done,
strict mode can be set back to true.


### The Configuration model
A `ImmutableConfigurationSection` successfully analyzed results in multiple objects described below:

![Application Identity Configuration](docs/ApplicationIdentityConfiguration.png)

Two things the diagram makes plain. `ApplicationIdentityLocalConfiguration` is **not** a party
configuration - it derives straight from the base, which is why a `"Local"` section carries settings
and not an identity. And `AllowFeatures` / `DisallowFeatures` / `AssemblyConfiguration` sit on the base
class, so they exist at every level and are what the inheritance described above actually propagates.

This model shows the 3 main types of identity objects:
- **ApplicationIdentity** is the root object.
- It holds a collection of **TenantDomains** and a collection of **Remotes**.
- A **TenantDomain** also holds a collection of **Remotes**.
- All configurations are issued and expose their `ImmutableConfigurationSection` configuration and
  a `AssemblyConfiguration` that captures possible "plugins/dynamic extension" assemblies.

The "runtime" model is initialized from these configuration objects. It is described by
[CK.AppIdentity.Abstractions](../CK.AppIdentity.Abstractions/README.md), which depends on this package.
