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
/// A fake client feature type and a client feature type derived from it ("Fake" and "FakeDerived").
/// </summary>
public class FakeClientFeature : ClientFeature
{
    public FakeClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

public sealed class FakeDerivedClientFeature : FakeClientFeature
{
    public FakeDerivedClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

// An option of every FakeClientFeature, and one of the derived feature only, that comes after it.
[PartyClientOption]
public sealed class CachingOption : IPartyComponentOption<FakeClientFeature>
{
}

[PartyClientOption, Requires<CachingOption>]
public sealed class DerivedOnlyOption : IPartyComponentOption<FakeDerivedClientFeature>
{
}

/// <summary>
/// Like the drivers of real client features, they refuse a key of the feature section that is not an option of the
/// feature: the options are selected by the PartyClient feature.
/// </summary>
public sealed class FakeClientFeatureDriver : ClientFeatureDriver<FakeClientFeature>
{
    readonly PartyComponentOptionDirectory _options;

    public FakeClientFeatureDriver( PartyComponentOptionDirectory options ) => _options = options;

    protected override FakeClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
    {
        return FakeDrivers.CheckOnlyOptions( monitor, _options, typeof( FakeClientFeature ), configuration )
                ? new FakeClientFeature( client, configuration )
                : null;
    }
}

public sealed class FakeDerivedClientFeatureDriver : ClientFeatureDriver<FakeDerivedClientFeature>
{
    readonly PartyComponentOptionDirectory _options;

    public FakeDerivedClientFeatureDriver( PartyComponentOptionDirectory options ) => _options = options;

    protected override FakeDerivedClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
    {
        return FakeDrivers.CheckOnlyOptions( monitor, _options, typeof( FakeDerivedClientFeature ), configuration )
                ? new FakeDerivedClientFeature( client, configuration )
                : null;
    }
}

static class FakeDrivers
{
    public static bool CheckOnlyOptions( IActivityMonitor monitor, PartyComponentOptionDirectory options, System.Type featureType, ImmutableConfigurationSection configuration )
    {
        var unknown = configuration.GetChildren().Where( c => !options.IsOptionName( featureType, c.Key ) ).Select( c => c.Key ).ToList();
        if( unknown.Count > 0 )
        {
            monitor.Error( $"Unexpected '{unknown.Concatenate( "', '" )}' in \"{configuration.Path}\": a fake feature has only options." );
            return false;
        }
        return true;
    }
}

/// <summary>
/// The requirements: an abstraction (unique), a feature that implements it, another one that implements it too, a
/// feature that requires it and a feature that requires the (not allowed by default) FakeFeatureDriver.
/// </summary>
public interface IFakeBuild : IPartyComponentAbstraction
{
}

public sealed class BuildClientFeature : ClientFeature, IFakeBuild
{
    public BuildClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

public sealed class OtherBuildClientFeature : ClientFeature, IFakeBuild
{
    public OtherBuildClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

[Requires<IFakeBuild>]
public sealed class ServeClientFeature : ClientFeature
{
    public ServeClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }

    protected override void ContributeDescription( ClientDescriptionBuilder builder ) => builder.RequestPath = "/";
}

[Requires<FakeFeatureDriver>]
public sealed class NeedsFakeClientFeature : ClientFeature
{
    public NeedsFakeClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

public sealed class BuildClientFeatureDriver : ClientFeatureDriver<BuildClientFeature>
{
    protected override BuildClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
}

public sealed class OtherBuildClientFeatureDriver : ClientFeatureDriver<OtherBuildClientFeature>
{
    protected override OtherBuildClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
}

public sealed class ServeClientFeatureDriver : ClientFeatureDriver<ServeClientFeature>
{
    protected override ServeClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
}

public sealed class NeedsFakeClientFeatureDriver : ClientFeatureDriver<NeedsFakeClientFeature>
{
    protected override NeedsFakeClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
}