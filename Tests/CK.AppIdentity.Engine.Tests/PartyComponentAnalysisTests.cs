using CK.AppIdentity;
using CK.Core;
using CK.Engine.TypeCollector;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Engine.Tests;

/// <summary>
/// The names, the optionality and the sides of the feature drivers (the PartyComponentDirectory content).
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

    public sealed class OptInFeatureDriver : NoopDriver { public OptInFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( IsOptional = false )]
    public sealed class ByDefaultFeatureDriver : NoopDriver { public ByDefaultFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Center", Side = FeatureSide.Local )]
    [Requires<RemoteCenterFeatureDriver>]
    public sealed class LocalCenterFeatureDriver : NoopDriver { public LocalCenterFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Center", Side = FeatureSide.Remote )]
    public sealed class RemoteCenterFeatureDriver : NoopDriver { public RemoteCenterFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    static PartyComponentAnalysis.Result? Analyze( params Type[] types )
    {
        var cache = new GlobalTypeCache();
        return PartyComponentAnalysis.Analyze( TestHelper.Monitor, types.Select( cache.Get ) );
    }

    [Test]
    public void the_names_the_optionality_and_the_sides()
    {
        var r = Analyze( typeof( OptInFeatureDriver ), typeof( ByDefaultFeatureDriver ), typeof( LocalCenterFeatureDriver ), typeof( RemoteCenterFeatureDriver ) ).ShouldNotBeNull();
        var byType = r.Components.ToDictionary( c => c.ComponentType );
        byType[typeof( OptInFeatureDriver )].FeatureName.ShouldBe( "OptIn" );
        byType[typeof( OptInFeatureDriver )].IsOptional.ShouldBeTrue();
        byType[typeof( ByDefaultFeatureDriver )].IsOptional.ShouldBeFalse();
        // The two sides have the name of the feature (their package names are "Center" and "CenterRemote").
        byType[typeof( LocalCenterFeatureDriver )].FeatureName.ShouldBe( "Center" );
        byType[typeof( LocalCenterFeatureDriver )].Side.ShouldBe( FeatureSide.Local );
        byType[typeof( RemoteCenterFeatureDriver )].FeatureName.ShouldBe( "Center" );
        byType[typeof( RemoteCenterFeatureDriver )].Side.ShouldBe( FeatureSide.Remote );
        // The Local side requires the Remote side: it comes after it.
        r.Components.ToList().IndexOf( byType[typeof( LocalCenterFeatureDriver )] )
            .ShouldBeGreaterThan( r.Components.ToList().IndexOf( byType[typeof( RemoteCenterFeatureDriver )] ) );
    }

    [PartyFeature( Side = FeatureSide.Local )]
    public sealed class LocalNoNameFeatureDriver : NoopDriver { public LocalNoNameFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Mixed", Side = FeatureSide.Local )]
    public sealed class LocalMixedFeatureDriver : NoopDriver { public LocalMixedFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "Mixed", Side = FeatureSide.Remote, IsOptional = false )]
    public sealed class RemoteMixedFeatureDriver : NoopDriver { public RemoteMixedFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [PartyFeature( "OptIn" )]
    public sealed class OtherOptInFeatureDriver : NoopDriver { public OtherOptInFeatureDriver( ApplicationIdentityService s ) : base( s ) { } }

    [TestCase( "NoName", "is the Local side of an asymmetric feature: its [PartyFeature] must give the feature name" )]
    [TestCase( "Mixed", "must agree on their optionality" )]
    [TestCase( "SameName", "a name can be borne by more than one package only when all of them are IsMultiple" )]
    public void the_errors( string kind, string expected )
    {
        Type[] types = kind switch
        {
            "NoName" => [typeof( LocalNoNameFeatureDriver )],
            "Mixed" => [typeof( LocalMixedFeatureDriver ), typeof( RemoteMixedFeatureDriver )],
            _ => [typeof( OptInFeatureDriver ), typeof( OtherOptInFeatureDriver )]
        };
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            if( kind == "SameName" )
            {
                // The names are unique among the packages of a BinPath (the engine checks them with the type sets).
                var cache = new GlobalTypeCache();
                FeaturePackages.CollectFeatureNames( TestHelper.Monitor, types.Select( cache.Get ) ).ShouldBeNull();
            }
            else
            {
                Analyze( types ).ShouldBeNull();
            }
            texts.ShouldContain( t => t.Contains( expected ) );
        }
    }
}
