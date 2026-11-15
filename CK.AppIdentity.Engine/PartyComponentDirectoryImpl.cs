using CK.AppIdentity;
using CK.CodeGen;
using CK.Core;
using System;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// Code generator of the <see cref="PartyComponentDirectory"/>: its content is computed by <see cref="PartyComponentAnalysis"/>
/// on the types of the BinPath (the concrete party components, their requirements, their abstractions, their order, their
/// feature names, their optionality and the sides of the asymmetric features).
/// </summary>
public sealed class PartyComponentDirectoryImpl : CSCodeGeneratorType
{
    /// <inheritdoc />
    public override CSCodeGenerationResult Implement( IActivityMonitor monitor, Type classType, ICSCodeGenerationContext c, ITypeScope scope )
    {
        var group = c.CurrentRun.ConfigurationGroup;
        // Skips the purely unified BinPath.
        if( group.IsUnifiedPure ) return CSCodeGenerationResult.Success;

        var result = PartyComponentAnalysis.Analyze( monitor, group.TypeSet );
        if( result == null ) return CSCodeGenerationResult.Failed;

        scope.Append( "public " ).Append( scope.Name )
             .Append( "() : base( new global::CK.AppIdentity.PartyComponentInfo[] {" ).NewLine();
        foreach( var i in result.Components )
        {
            scope.Append( "new global::CK.AppIdentity.PartyComponentInfo( typeof(" ).Append( i.ComponentType.ToGlobalTypeName() ).Append( "), new global::System.Type[] {" );
            foreach( var r in i.Requires ) scope.Append( "typeof(" ).Append( r.ToGlobalTypeName() ).Append( ")," );
            scope.Append( "}, new global::System.Type[] {" );
            foreach( var a in i.Abstractions ) scope.Append( "typeof(" ).Append( a.ToGlobalTypeName() ).Append( ")," );
            scope.Append( "}, " ).AppendSourceString( i.FeatureName )
                 .Append( ", " ).Append( i.IsOptional )
                 .Append( ", global::CK.AppIdentity.FeatureSide." ).Append( i.Side.ToString() ).Append( " )," ).NewLine();
        }
        scope.Append( "}, new global::System.Collections.Generic.HashSet<global::System.Type>() {" );
        foreach( var m in result.MultipleAbstractions.OrderBy( m => m.FullName, StringComparer.Ordinal ) ) scope.Append( "typeof(" ).Append( m.ToGlobalTypeName() ).Append( ")," );
        scope.Append( "} ) {}" ).NewLine();
        monitor.Info( $"{result.Components.Count} party component(s): '{result.Components.Select( i => i.ComponentType.Name ).Concatenate( "', '" )}'." );
        return CSCodeGenerationResult.Success;
    }
}
