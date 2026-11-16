using CK.AppIdentity;
using CK.Core;
using CK.TypeCache;
using CK.TypeSet;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests;

/// <summary>
/// The names, the activation and the sides of the feature drivers (the PartyComponentDirectory content).
/// </summary>
[TestFixture]
public class PartyComponentAnalysisTests
{
    public abstract class NoopDriver : ApplicationIdentityFeatureDriver
    {
        protected NoopDriver( ApplicationIdentityService s ) : base( s ) { }
        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );
        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );
        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;
        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    [PartyFeature( "OptIn", TypeActivation.Optional )]
    public sealed class OptInFeatureDriver : NoopDriver { public OptInFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "ByDefault", TypeActivation.ByDefault )]
    public sealed class ByDefaultFeatureDriver : NoopDriver { public ByDefaultFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Center", TypeActivation.Optional, Side = FeatureSide.Local )]
    [Requires<RemoteCenterFeatureDriver>]
    public sealed class LocalCenterFeatureDriver : NoopDriver { public LocalCenterFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Center", TypeActivation.Optional, Side = FeatureSide.Remote )]
    public sealed class RemoteCenterFeatureDriver : NoopDriver { public RemoteCenterFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    static PartyComponentAnalysis.Result? Analyze( params Type[] types )
    {
        var cache = new GlobalTypeCache();
        return PartyComponentAnalysis.Analyze( TestHelper.Monitor, types.Select( cache.Get ) );
    }

    [Test]
    public void the_names_the_activation_and_the_sides()
    {
        var r = Analyze( typeof( OptInFeatureDriver ), typeof( ByDefaultFeatureDriver ), typeof( LocalCenterFeatureDriver ), typeof( RemoteCenterFeatureDriver ) ).ShouldNotBeNull();
        var byType = r.Components.ToDictionary( c => c.ComponentType );
        byType[typeof( OptInFeatureDriver )].FeatureName.ShouldBe( "OptIn" );
        byType[typeof( OptInFeatureDriver )].IsOptional.ShouldBeTrue();
        byType[typeof( ByDefaultFeatureDriver )].IsOptional.ShouldBeFalse();
        byType[typeof( ByDefaultFeatureDriver )].Activation.ShouldBe( TypeActivation.ByDefault );
        // The two sides have the name of the feature (their configuration names are "Center" and "CenterRemote").
        byType[typeof( LocalCenterFeatureDriver )].FeatureName.ShouldBe( "Center" );
        byType[typeof( LocalCenterFeatureDriver )].Side.ShouldBe( FeatureSide.Local );
        byType[typeof( RemoteCenterFeatureDriver )].FeatureName.ShouldBe( "Center" );
        byType[typeof( RemoteCenterFeatureDriver )].Side.ShouldBe( FeatureSide.Remote );
        // The Local side requires the Remote side: it comes after it.
        r.Components.ToList().IndexOf( byType[typeof( LocalCenterFeatureDriver )] )
            .ShouldBeGreaterThan( r.Components.ToList().IndexOf( byType[typeof( RemoteCenterFeatureDriver )] ) );
    }

    public sealed class UndeclaredFeatureDriver : NoopDriver { public UndeclaredFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Mixed", TypeActivation.Optional, Side = FeatureSide.Local )]
    public sealed class LocalMixedFeatureDriver : NoopDriver { public LocalMixedFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Mixed", TypeActivation.ByDefault, Side = FeatureSide.Remote )]
    public sealed class RemoteMixedFeatureDriver : NoopDriver { public RemoteMixedFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "OptIn", TypeActivation.Optional )]
    public sealed class OtherOptInFeatureDriver : NoopDriver { public OtherOptInFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    // The configuration name of the Remote side is "CenterRemote": it is in the same scope as the other features.
    [PartyFeature( "CenterRemote", TypeActivation.Optional )]
    public sealed class CenterRemoteFeatureDriver : NoopDriver { public CenterRemoteFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [TestCase( "Undeclared", "UndeclaredFeatureDriver' must declare its feature" )]
    [TestCase( "Mixed", "must agree on their activation" )]
    [TestCase( "SameName", "have the same name 'OptIn': a name is unique among the features of a party" )]
    [TestCase( "SameAsRemote", "have the same name 'CenterRemote'" )]
    public void the_errors( string kind, string expected )
    {
        Type[] types = kind switch
        {
            "Undeclared" => [typeof( UndeclaredFeatureDriver )],
            "Mixed" => [typeof( LocalMixedFeatureDriver ), typeof( RemoteMixedFeatureDriver )],
            "SameAsRemote" => [typeof( LocalCenterFeatureDriver ), typeof( RemoteCenterFeatureDriver ), typeof( CenterRemoteFeatureDriver )],
            _ => [typeof( OptInFeatureDriver ), typeof( OtherOptInFeatureDriver )]
        };
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            Analyze( types ).ShouldBeNull();
            texts.ShouldContain( t => t.Contains( expected ) );
        }
    }
}
