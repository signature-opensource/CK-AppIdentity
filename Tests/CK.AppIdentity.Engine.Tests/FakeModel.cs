using CK.Core;
using System.Collections.Generic;
using System.Linq;
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

// A non optional [RequiredBy<T>]: Main requires Tracing (Tracing comes first and can be enabled alone).
// Used only by its own test (not in the common model).
[FeatureOption, RequiredBy<MainOption>]
public sealed class TracingOption : IPartyComponentOption<FakeFeatureDriver>
{
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

/// <summary>
/// Builds the "Fake" client (a <see cref="FakeClient"/>) and the "Derived" one (a <see cref="FakeDerivedClient"/>). Like
/// the factories of real clients, it refuses a key of the client section that is not an option of the client: the
/// options are selected by the PartyClient feature.
/// </summary>
public sealed class FakeClientFactory : IPartyClientFactory
{
    readonly PartyComponentOptionDirectory _options;

    public FakeClientFactory( PartyComponentOptionDirectory options ) => _options = options;

    public IReadOnlyCollection<string> ClientNames => ["Fake", "Derived"];

    public IReadOnlyList<ApplicationIdentityFeatureDriver> RequiredFeatures => [];

    public PartyClient? Create( IActivityMonitor monitor, ILocalParty party, string name, ImmutableConfigurationSection configuration )
    {
        var type = name == "Derived" ? typeof( FakeDerivedClient ) : typeof( FakeClient );
        var unknown = configuration.GetChildren().Where( c => !_options.IsOptionName( type, c.Key ) ).Select( c => c.Key ).ToList();
        if( unknown.Count > 0 )
        {
            monitor.Error( $"Unexpected '{unknown.Concatenate( "', '" )}' in \"Local:PartyClient:{name}\": a fake client has only options." );
            return null;
        }
        return name == "Derived"
                ? new FakeDerivedClient( party, name, configuration )
                : new FakeClient( party, name, configuration );
    }
}
