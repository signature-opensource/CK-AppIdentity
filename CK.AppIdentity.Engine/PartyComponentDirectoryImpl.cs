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
        // The types drive: the keys of the projected party must name members of their type sets.
        if( !ProjectionCheck.Run( monitor, group, result ) ) return CSCodeGenerationResult.Failed;

        scope.Append( "public " ).Append( scope.Name )
             .Append( "() : base( new global::CK.AppIdentity.PartyComponentInfo[] {" ).NewLine();
        // The models of the components ([Configuration<TModel>]): their description, generated (no reflection at run time).
        var models = new ConfigurationModelCodeWriter( scope );
        foreach( var i in result.Components )
        {
            // The model is the one of the analysis: its description is generated.
            var modelType = i.Model?.ModelType;
            string? modelDescription = null;
            if( modelType != null && !models.TryGetDescription( monitor, modelType, out modelDescription ) )
            {
                monitor.Error( $"Invalid configuration model of the party component '{i.ComponentType.ToCSharpName()}'." );
                return CSCodeGenerationResult.Failed;
            }
            scope.Append( "new global::CK.AppIdentity.PartyComponentInfo( typeof(" ).Append( i.ComponentType.ToGlobalTypeName() ).Append( "), new global::System.Type[] {" );
            foreach( var r in i.Requires ) scope.Append( "typeof(" ).Append( r.ToGlobalTypeName() ).Append( ")," );
            scope.Append( "}, new global::System.Type[] {" );
            foreach( var a in i.Abstractions ) scope.Append( "typeof(" ).Append( a.ToGlobalTypeName() ).Append( ")," );
            scope.Append( "}, " ).AppendSourceString( i.FeatureName )
                 .Append( ", global::CK.Core.TypeActivation." ).Append( i.Activation.ToString() )
                 .Append( ", global::CK.AppIdentity.FeatureSide." ).Append( i.Side.ToString() )
                 .Append( ", " ).Append( modelDescription ?? "null" );
            scope.Append( " )," ).NewLine();
        }
        scope.Append( "}, new global::System.Collections.Generic.HashSet<global::System.Type>() {" );
        foreach( var m in result.MultipleAbstractions.OrderBy( m => m.FullName, StringComparer.Ordinal ) ) scope.Append( "typeof(" ).Append( m.ToGlobalTypeName() ).Append( ")," );
        // The party components that are members of each type set (a local party, a client): the types drive.
        scope.Append( "}, new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlySet<global::System.Type>>() {" ).NewLine();
        foreach( var (name, set) in group.TypeSets.OrderBy( kv => kv.Key, StringComparer.Ordinal ) )
        {
            var members = set.Select( t => t.Type ).ToHashSet();
            scope.Append( "{ " ).AppendSourceString( name ).Append( ", new global::System.Collections.Generic.HashSet<global::System.Type>() {" );
            foreach( var i in result.Components.Where( i => members.Contains( i.ComponentType ) ) ) scope.Append( "typeof(" ).Append( i.ComponentType.ToGlobalTypeName() ).Append( ")," );
            scope.Append( "} }," ).NewLine();
        }
        scope.Append( "} ) {}" ).NewLine();
        monitor.Info( $"{result.Components.Count} party component(s): '{result.Components.Select( i => i.ComponentType.Name ).Concatenate( "', '" )}'." );
        return CSCodeGenerationResult.Success;
    }
}
