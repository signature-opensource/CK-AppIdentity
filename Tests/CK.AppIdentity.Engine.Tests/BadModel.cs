using CK.AppIdentity.Engine.Tests.Fake;
using CK.Core;

namespace CK.AppIdentity.Engine.Tests.Bad;

// Options that the engine rejects: they are only registered by the tests that check the errors.

public sealed class NoAttributeOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

[PartyClientFeatureOption( "WrongKind", TypeActivation.Optional )]
public sealed class WrongKindOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

[PartyFeatureOption( "TwoConstructors", TypeActivation.Optional )]
public sealed class TwoConstructorsOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public TwoConstructorsOption() { }

    public TwoConstructorsOption( FakeService service ) { }
}

[PartyFeatureOption( "Main", TypeActivation.Optional )]
public sealed class MainOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

[PartyFeatureOption( "TwoConfigurations", TypeActivation.Optional )]
public sealed class TwoConfigurationsOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public TwoConfigurationsOption( Microsoft.Extensions.Configuration.IConfiguration a, Microsoft.Extensions.Configuration.IConfiguration b ) { }
}

[PartyFeatureOption( "Section", TypeActivation.Optional )]
public sealed class SectionOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public SectionOption( Microsoft.Extensions.Configuration.IConfigurationSection section ) { }
}
