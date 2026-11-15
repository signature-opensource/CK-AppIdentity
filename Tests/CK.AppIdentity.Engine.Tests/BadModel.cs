using CK.AppIdentity.Engine.Tests.Fake;

namespace CK.AppIdentity.Engine.Tests.Bad;

// Options that the engine rejects: they are only registered by the tests that check the errors.

public sealed class NoAttributeOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

[PartyClientOption]
public sealed class WrongKindOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

[FeatureOption]
public sealed class TwoConstructorsOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public TwoConstructorsOption() { }

    public TwoConstructorsOption( FakeService service ) { }
}

[FeatureOption]
public sealed class MainOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

[FeatureOption]
public sealed class TwoConfigurationsOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public TwoConfigurationsOption( Microsoft.Extensions.Configuration.IConfiguration a, Microsoft.Extensions.Configuration.IConfiguration b ) { }
}

[FeatureOption]
public sealed class SectionOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public SectionOption( Microsoft.Extensions.Configuration.IConfigurationSection section ) { }
}
