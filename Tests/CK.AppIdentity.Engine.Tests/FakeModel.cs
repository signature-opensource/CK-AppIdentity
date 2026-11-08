using CK.Core;
using System.Threading.Tasks;

namespace CK.AppIdentity.Engine.Tests.Fake;

/// <summary>
/// A fake feature driver: the owner of the options below.
/// </summary>
public sealed class FakeFeatureDriver : ApplicationIdentityFeatureDriver
{
    public FakeFeatureDriver( ApplicationIdentityService s ) : base( s, isAllowedByDefault: false ) { }

    protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );

    protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

    protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

    protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
}

/// <summary>
/// A service that an option receives in its constructor.
/// </summary>
public sealed class FakeService
{
    public string Name => "The fake service.";
}

// The order: Header (before Main, order only), Main, Compression (after Main, only with it).
[FeatureOption]
public sealed class MainOption : IPartyComponentOption<FakeFeatureDriver>
{
}

[FeatureOption( IsEnabledByDefault = true ), OptionalRequiredBy<MainOption>]
public sealed class HeaderOption : IPartyComponentOption<FakeFeatureDriver>
{
}

[FeatureOption( IsEnabledByDefault = true ), Requires<MainOption>]
public sealed class CompressionOption : IPartyComponentOption<FakeFeatureDriver>
{
    public CompressionOption( FakeService service ) => Service = service;

    public FakeService Service { get; }
}

/// <summary>
/// A fake client type and a client type derived from it.
/// </summary>
public class FakeClient : PartyClient
{
    public FakeClient( ILocalParty party, string name, ImmutableConfigurationSection configuration ) : base( party, name, configuration ) { }
}

public sealed class FakeDerivedClient : FakeClient
{
    public FakeDerivedClient( ILocalParty party, string name, ImmutableConfigurationSection configuration ) : base( party, name, configuration ) { }
}

// An option of every FakeClient, and one of the derived client only, that comes after it.
[PartyClientOption]
public sealed class CachingOption : IPartyComponentOption<FakeClient>
{
}

[PartyClientOption, Requires<CachingOption>]
public sealed class DerivedOnlyOption : IPartyComponentOption<FakeDerivedClient>
{
}
