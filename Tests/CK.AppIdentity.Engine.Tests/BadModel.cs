using CK.AppIdentity.Engine.Tests.Fake;

namespace CK.AppIdentity.Engine.Tests.Bad;

// Options that the engine rejects: they are only registered by the tests that check the errors.

public sealed class NoAttributeOption : IPartyComponentOption<FakeFeatureDriver>
{
}

[PartyClientOption]
public sealed class WrongKindOption : IPartyComponentOption<FakeFeatureDriver>
{
}

[FeatureOption]
public sealed class TwoConstructorsOption : IPartyComponentOption<FakeFeatureDriver>
{
    public TwoConstructorsOption() { }

    public TwoConstructorsOption( FakeService service ) { }
}

[FeatureOption]
public sealed class MainOption : IPartyComponentOption<FakeFeatureDriver>
{
}
