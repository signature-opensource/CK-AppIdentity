# The options of the party components

A party component (a feature driver, a client feature) does one thing, and other packages may want to add something to
it: a transport that understands forwarded headers, a client that is served with a compression... Each of these is an
**option** of the component: a small class that a package brings, that a party enables or disables in its
configuration, and that the component applies in a known order. The component doesn't know its options: the options
know their component.

## An option is a class

An option implements `IPartyComponentOption<TOwner>` (CK.AppIdentity.Abstractions) where `TOwner` is the party
component it applies to, and is decorated with the attribute of its kind of owner. They are the party component level
of the options of the configurable types (`IConfigurableOption<TOwner>` and `IConfigurableOptionAttribute` of
CK.Abstractions: the App aspects and the AppContext aspects have options too).

| Owner | Attribute | Package |
|---|---|---|
| A feature driver (`IApplicationIdentityFeatureDriver`) | `[PartyFeatureOption]` | CK.AppIdentity.Abstractions |
| A client feature (`ClientFeature`) | `[PartyClientFeatureOption]` | CK.AppIdentity.PartyClient |

```csharp
[PartyFeatureOption( "Compression", TypeActivation.ByDefault ), Requires<MainOption>]
public sealed class CompressionOption : IPartyComponentOption<FakeFeatureDriver>
{
    public CompressionOption( IConfiguration? configuration, FakeService service )
    {
        var level = configuration?["Level"];
        Level = level != null ? int.Parse( level ) : 1; // An invalid value throws.
        Service = service;
    }

    public int Level { get; }

    public FakeService Service { get; }
}
```

- **An option is a configurable type** (`IConfigurableTypeAttribute`, CK.ResourceSpace.Abstractions): its attribute
  declares its name and its activation explicitly.
- **Its name** ("Compression") is its key in the owner's configuration section, unique among the options of an owner.
- **An option applies to its owner type and to the types derived from it**: an option of a base client feature is an option of
  every client derived from it.
- **Its activation**: `TypeActivation.Optional` makes it opt-in (enabled only when the owner's configuration says so),
  `ByDefault` opt-out (enabled unless it says `false`), `Required` always enabled.
- **An option declares its data**: its single public constructor reads its configuration, its `IConfiguration`
  parameter (a plain one, at most one), into typed properties. The configuration is null when the option is enabled by
  default or by `true`. **An invalid value is an exception**: the configuration is meant to be validated before it is
  used (by a "configurator"), a bad value at run time is exceptional. The other parameters are services of its owner.
- **An option is not an auto service**: it is created with its owner's party (a feature driver) or with its owner (a
  client feature), and held by it.
- **Every owner holds its options**: a feature driver holds the options of each party
  (`ApplicationIdentityFeatureDriver.GetOptions( party )`, `GetOption<T>( party )`), created before it sets the party up
  (an option that can't be created fails the setup); a client feature holds its own ones (`ClientFeature.Options`,
  `GetOption<T>()`), created with it. A service of the package that brings an option asks the owner for it: it never reads
  the configuration itself.
- **What applying an option means is the owner's**: when the owner acts on its options, it defines their base type or
  their interface and calls them.

### An option with a configuration model

An option may declare its configuration model (`[Configuration<TModel>]`, CK.ConfigurationModel): its constructor takes
the model instead of an `IConfiguration`. The model is read key by key along the levels of the party (its section in
the feature's section of the party, then in each `"Defaults"` above it: `ApplicationIdentityConfiguration.GetFeatureSections`),
validated, and its errors logged (an invalid model is an error: the option is not created). The engine generates the description of
the model and its creation (CK.ConfigurationModel.Engine): no reflection at run time; the model must be public. Its
owner can have a model too ([a feature](../Features/README.md#a-feature-with-a-configuration-model), a client feature):
the names of its options are reserved keys of the owner's model.

## The order and the requirements

The options of an owner are ordered by the topology attributes between them: `[Requires<T>]`, `[RequiredBy<T>]` and
their optional forms (`[OptionalRequires<T>]`, `[OptionalRequiredBy<T>]`).

- The direction of a relation gives the order: a required option comes before the one that requires it.
- A non optional relation also means "only with it": the requiring option can't be enabled without the required one.
  `[Requires<T>]` on X: X requires T. `[RequiredBy<T>]` on X: T requires X (X can be enabled alone). An optional
  relation is an order only.
- A relation to a class that is not an option of the same owner is ignored.

The engine (CK.AppIdentity.Engine) computes this order and these requirements for each owner type of the application:
see [CK.AppIdentity.Engine](../../CK.AppIdentity.Engine/README.md).

## The directory

[`PartyComponentOptionDirectory`](PartyComponentOptionDirectory.cs) is the singleton that the engine generates:

- `OwnerTypes`: the owner types that have options.
- `GetOptions( ownerType )`: the options of an owner type ([`PartyComponentOptionInfo`](PartyComponentOptionInfo.cs):
  `Name`, `OptionType`, `Activation`, `Requires`), in their order. `PartyComponentOptionInfo.Create( services,
  configuration )` creates an option, without reflection.
- `IsOptionName( ownerType, key )`: whether a key of the owner's section is the name of one of its options.
- `TrySelect( monitor, ownerType, ownerSection, out selected )`: the options that the owner's configuration enables, in
  their order, each with its configuration section (a `SelectedConfigurationOption<PartyComponentOptionInfo>`).
- `TryCreate( monitor, ownerType, ownerSection, services, out created )`: selects the options and creates them. An
  option that throws is an error, logged with its configuration path and the exception message ("Unable to create the option '...': ...").

The configuration of an option of a feature driver is looked up in the feature's section of the party, then in the
"Defaults" above it: each option on its own, the nearest wins (`"Defaults": { "Fake": { "Compression": { "Level": 3 } } }`
applies to every party below that doesn't configure "Compression").

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

`PartyComponentOptionDirectory.TrySelect` applies the rules of `ConfigurationOptionSelector` (CK.Configuration: a
`PartyComponentOptionInfo` is an `IConfigurationOption`):

| Case | Result |
|---|---|
| The key is absent | The option's default (enabled unless its `Activation` is `Optional`). |
| `true` or `false` | Enabled or disabled. A redundant `true` (`ByDefault`, `Required`) or `false` (`Optional`) is a warning. |
| `false` on a `Required` option | An error (a required option is always enabled). |
| A section | Enabled, with this section as its configuration. |
| Any other value | An error. |
| An option enabled in the configuration (or required) that requires a disabled one | An error. |
| An option enabled by default that requires a disabled one | Disabled with it (an information is logged), and so on. |
