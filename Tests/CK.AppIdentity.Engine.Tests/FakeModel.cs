using CK.Core;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.AppIdentity.Engine.Tests.Fake;

/// <summary>
/// A fake feature driver: the owner of the options below.
/// </summary>
[PartyFeature( "FakeParty", TypeActivation.Optional )]
public sealed class FakePartyFeatureDriver : ApplicationIdentityFeatureDriver
{
    public FakePartyFeatureDriver( ApplicationIdentityService s ) : base( s ) { }

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
// Main reads its configuration: { "Level": 3 } (1 by default). An invalid level throws.
[PartyFeatureOption( "Main", TypeActivation.Optional )]
public sealed class MainOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public MainOption( IConfiguration? configuration )
    {
        var level = configuration?["Level"];
        if( level != null && !int.TryParse( level, out _ ) ) Throw.ArgumentException( nameof( configuration ), $"Level must be an integer, not '{level}'." );
        Level = level != null ? int.Parse( level ) : 1;
    }

    public int Level { get; }
}

[PartyFeatureOption( "Header", TypeActivation.ByDefault ), OptionalRequiredBy<MainOption>]
public sealed class HeaderOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

[PartyFeatureOption( "Compression", TypeActivation.ByDefault ), Requires<MainOption>]
public sealed class CompressionOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public CompressionOption( FakeService service ) => Service = service;

    public FakeService Service { get; }
}

// Another party feature, and an option of FakeParty that requires it (outside the options of its owner: the party
// feature of the party of its owner, "only with it"). Used only by its own test (not in the common model).
[PartyFeature( "FakeMail", TypeActivation.Optional )]
public sealed class FakeMailFeatureDriver : ApplicationIdentityFeatureDriver
{
    public FakeMailFeatureDriver( ApplicationIdentityService s ) : base( s ) { }

    protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );

    protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

    protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

    protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
}

[PartyFeatureOption( "Notify", TypeActivation.ByDefault ), Requires<FakeMailFeatureDriver>]
public sealed class NotifyOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

// A non optional [RequiredBy<T>]: Main requires Tracing (Tracing comes first and can be enabled alone).
// Used only by its own test (not in the common model).
[PartyFeatureOption( "Tracing", TypeActivation.Optional ), RequiredBy<MainOption>]
public sealed class TracingOption : IPartyComponentOption<FakePartyFeatureDriver>
{
}

/// <summary>
/// A fake client feature type and a client feature type derived from it ("Fake" and "FakeDerived").
/// </summary>
[PartyClientFeature( "Fake", TypeActivation.Optional )]
public class FakeClientFeature : ClientFeature
{
    public FakeClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

[PartyClientFeature( "FakeDerived", TypeActivation.Optional )]
public sealed class FakeDerivedClientFeature : FakeClientFeature
{
    public FakeDerivedClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

// An option of every FakeClientFeature, and one of the derived feature only, that comes after it.
[PartyClientFeatureOption( "Caching", TypeActivation.Optional )]
public sealed class CachingOption : IPartyComponentOption<FakeClientFeature>
{
}

[PartyClientFeatureOption( "DerivedOnly", TypeActivation.Optional ), Requires<CachingOption>]
public sealed class DerivedOnlyOption : IPartyComponentOption<FakeDerivedClientFeature>
{
}

/// <summary>
/// The fake client features have no configuration model: their section has only their options (the PartyClient feature
/// refuses any other key and selects the options).
/// </summary>
public sealed class FakeClientFeatureDriver : ClientFeatureDriver<FakeClientFeature>
{
    protected override FakeClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
}

public sealed class FakeDerivedClientFeatureDriver : ClientFeatureDriver<FakeDerivedClientFeature>
{
    protected override FakeDerivedClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
}

/// <summary>
/// The requirements: an abstraction (unique), a feature that implements it, another one that implements it too, a
/// feature that requires it and a feature that requires the (not allowed by default) FakePartyFeatureDriver.
/// </summary>
[IsSingle]
public interface IFakeBuild : IPartyComponentAbstraction
{
}

/// <summary>
/// The compile time model of the <see cref="BuildClientFeature"/>: its keys are read by the build, ignored at run time.
/// </summary>
[ConfigurationModel]
public sealed class BuildCompileConfiguration
{
    public int? Port { get; init; }
}

[PartyClientFeature( "Build", TypeActivation.Optional )]
[CompileConfiguration<BuildCompileConfiguration>]
public sealed class BuildClientFeature : ClientFeature, IFakeBuild
{
    public BuildClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

/// <summary>
/// An option of "Build" that requires another feature of its client: "Serve" (created after "Build", that it requires).
/// </summary>
[PartyClientFeatureOption( "Window", TypeActivation.Optional ), Requires<ServeClientFeature>]
public sealed class WindowOption : IPartyComponentOption<BuildClientFeature>
{
}

[PartyClientFeature( "OtherBuild", TypeActivation.Optional )]
public sealed class OtherBuildClientFeature : ClientFeature, IFakeBuild
{
    public OtherBuildClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
}

[Requires<IFakeBuild>]
[PartyClientFeature( "Serve", TypeActivation.Optional )]
public sealed class ServeClientFeature : ClientFeature
{
    public ServeClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }

    protected override void ContributeDescription( ClientDescriptionBuilder builder ) => builder.RequestPath = "/";
}

[Requires<FakePartyFeatureDriver>]
[PartyClientFeature( "NeedsFake", TypeActivation.Optional )]
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
    // A feature finds the features that it requires (created before it).
    protected override ServeClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
        => client.GetFeature<IFakeBuild>() != null ? new( client, configuration ) : null;
}

public sealed class NeedsFakeClientFeatureDriver : ClientFeatureDriver<NeedsFakeClientFeature>
{
    protected override NeedsFakeClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
}

/// <summary>
/// The configuration of the <see cref="ModeledOption"/>.
/// </summary>
[ConfigurationModel]
public sealed class ModeledOptionConfiguration
{
    public required string Name { get; init; }

    public int Count { get; init; } = 1;
}

[PartyFeatureOption( "Modeled", TypeActivation.Optional )]
[Configuration<ModeledOptionConfiguration>]
public sealed class ModeledOption : IPartyComponentOption<FakePartyFeatureDriver>
{
    public ModeledOption( ModeledOptionConfiguration configuration ) => Configuration = configuration;

    public ModeledOptionConfiguration Configuration { get; }
}
