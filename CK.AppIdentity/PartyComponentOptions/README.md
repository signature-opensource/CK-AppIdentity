# The options of the party components

A party component (a feature driver, a party client) does one thing, and other packages may want to add something to
it: a transport that understands forwarded headers, a client that is served with a compression... Each of these is an
**option** of the component: a small class that a package brings, that a party enables or disables in its
configuration, and that the component applies in a known order. The component doesn't know its options: the options
know their component.

## An option is a class

An option implements `IPartyComponentOption<TOwner>` (CK.AppIdentity.Abstractions) where `TOwner` is the party
component it applies to, and is decorated with the attribute of its kind of owner:

| Owner | Attribute | Package |
|---|---|---|
| A feature driver (`IApplicationIdentityFeatureDriver`) | `[FeatureOption]` | CK.AppIdentity.Abstractions |
| A party client (`PartyClient`) | `[PartyClientOption]` | CK.AppIdentity.PartyClient |

```csharp
[FeatureOption( IsEnabledByDefault = true ), Requires<MainOption>]
public sealed class CompressionOption : IPartyComponentOption<FakeFeatureDriver>
{
    public CompressionOption( FakeService service ) => Service = service;

    public FakeService Service { get; }
}
```

- **Its name** is its type name without its "Option" suffix ("Compression"): it is its key in the owner's
  configuration section.
- **An option applies to its owner type and to the types derived from it**: an option of a base client is an option of
  every client derived from it.
- **An option is opt-in by default**: `IsEnabledByDefault = true` makes it opt-out.
- **An option is not an auto service**: it is created from the services of its owner when the owner applies its
  options (its single public constructor receives them), then forgotten.
- **What applying an option means is the owner's**: an owner defines the base type or the interface of its options.

## The order and the requirements

The options of an owner are ordered by the topology attributes between them: `[Requires<T>]`, `[RequiredBy<T>]` and
their optional forms (`[OptionalRequires<T>]`, `[OptionalRequiredBy<T>]`).

- The direction of a relation gives the order: a required option comes before the one that requires it.
- A non optional relation also means "only with it": the option can't be enabled without the other one. An optional
  relation is an order only.
- A relation to a class that is not an option of the same owner is ignored.

The engine (CK.AppIdentity.Engine) computes this order and these requirements for each owner type of the application:
see [CK.AppIdentity.Engine](../../CK.AppIdentity.Engine/README.md).

## The directory

[`PartyComponentOptionDirectory`](PartyComponentOptionDirectory.cs) is the singleton that the engine generates:

- `OwnerTypes`: the owner types that have options.
- `GetOptions( ownerType )`: the options of an owner type ([`PartyComponentOptionInfo`](PartyComponentOptionInfo.cs):
  `Name`, `OptionType`, `IsEnabledByDefault`, `Requires`), in their order. `PartyComponentOptionInfo.Create( services )`
  creates an option from the services of its owner, without reflection.
- `IsOptionName( ownerType, key )`: whether a key of the owner's section is the name of one of its options.
- `TrySelect( monitor, ownerType, ownerSection, out selected )`: the options that the owner's configuration enables, in
  their order, each with its configuration section (a `SelectedPartyComponentOption`).

## The configuration

In the owner's configuration section, the key of an option is its name:

```jsonc
{
  "Main": true,                 // Enabled.
  "Header": false,              // Disabled.
  "Compression": { "Level": 3 } // Enabled, with its configuration.
                                // An option that is not there: its default.
}
```

`PartyComponentOptionDirectory.TrySelect` applies these rules:

| Case | Result |
|---|---|
| The key is absent | The option's default (`IsEnabledByDefault`). |
| `true` or `false` | Enabled or disabled. |
| A section | Enabled, with this section as its configuration. |
| Any other value | An error. |
| An option enabled in the configuration that requires a disabled one | An error. |
| An option enabled by default that requires a disabled one | Disabled with it (an information is logged), and so on. |
