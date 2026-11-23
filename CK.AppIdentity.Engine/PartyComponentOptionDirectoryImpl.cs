using CK.AppIdentity;
using CK.CodeGen;
using CK.Core;
using CK.TypeCache;
using CK.TypeSet;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CK.Setup;

/// <summary>
/// Code generator of the <see cref="PartyComponentOptionDirectory"/>: the options of each party component owner type
/// of the BinPath, ordered by a ResourceSpace island per owner type.
/// <list type="bullet">
///   <item>An option is a concrete class that implements <see cref="IPartyComponentOption{TOwner}"/> (once), decorated
///   with the <see cref="IPartyComponentOptionAttribute"/> of its kind of owner, with one public constructor: its
///   <see cref="IConfiguration"/> parameter (at most one) is its configuration, the other ones are services.</item>
///   <item>The owners are the concrete party components of the BinPath to which an option applies (its owner type or a
///   type derived from it).</item>
///   <item>The island of an owner: its options are the allowed types, the compatible types are the options of an owner
///   type assignable from it (a relation to another option is ignored).</item>
/// </list>
/// </summary>
public sealed class PartyComponentOptionDirectoryImpl : CSCodeGeneratorType
{
    const string _attributeHint = "[PartyFeatureOption] for the options of a feature driver, [PartyClientFeatureOption] for the options of a client feature";

    /// <inheritdoc />
    public override CSCodeGenerationResult Implement( IActivityMonitor monitor, Type classType, ICSCodeGenerationContext c, ITypeScope scope )
    {
        var group = c.CurrentRun.ConfigurationGroup;
        // Skips the purely unified BinPath.
        if( group.IsUnifiedPure ) return CSCodeGenerationResult.Success;

        if( !ConfigurableOptions.TryCollect( monitor, group.TypeSet, [typeof( IPartyComponent )], _attributeHint, out var options ) ) return CSCodeGenerationResult.Failed;
        var owners = group.TypeSet.Select( t => t.Type )
                                  .Where( t => !t.IsAbstract
                                               && !t.IsInterface
                                               && typeof( IPartyComponent ).IsAssignableFrom( t )
                                               && options.Any( o => o.Owner.IsAssignableFrom( t ) ) )
                                  .OrderBy( t => t.FullName, StringComparer.Ordinal )
                                  .ToList();
        bool success = true;
        var islands = new List<(Type Owner, List<OrderedConfigurableOption> Options)>();
        foreach( var owner in owners )
        {
            using( monitor.OpenInfo( $"Ordering the options of '{owner.ToCSharpName()}'." ) )
            {
                var island = ConfigurableOptions.TryOrder( monitor,
                                                           group.TypeCache,
                                                           group.EngineConfiguration.RevertOrderingNames,
                                                           owner,
                                                           options.Where( o => o.Owner.IsAssignableFrom( owner ) ).ToList() );
                if( island == null ) success = false;
                else islands.Add( (owner, island) );
            }
        }
        if( !success ) return CSCodeGenerationResult.Failed;

        scope.Append( "public " ).Append( scope.Name )
             .Append( "() : base( new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Collections.Generic.IReadOnlyList<global::CK.AppIdentity.PartyComponentOptionInfo>>" ).NewLine()
             .Append( "{" ).NewLine();
        foreach( var (owner, island) in islands )
        {
            scope.Append( "{ typeof(" ).Append( owner.ToGlobalTypeName() ).Append( "), new global::CK.AppIdentity.PartyComponentOptionInfo[] {" ).NewLine();
            foreach( var (o, requires) in island )
            {
                var t = o.Type.Type;
                // An option is a resource group, not a StObj type: its assembly may host no StObj type at all (an
                // assembly of options only), it must be referenced by the generated code.
                scope.Workspace.EnsureAssemblyReference( t );
                scope.Append( "new global::CK.AppIdentity.PartyComponentOptionInfo( " )
                     .AppendSourceString( o.Name ).Append( ", typeof(" ).Append( t.ToGlobalTypeName() ).Append( "), " )
                     .Append( "global::CK.Core.TypeActivation." ).Append( o.Activation.ToString() ).Append( ", new string[] {" );
                foreach( var r in requires ) scope.AppendSourceString( r ).Append( "," );
                // The model of the option ([Configuration<TModel>]): the factory receives it instead of the section.
                var modelType = t.GetCustomAttributes( inherit: false )
                                 .Select( a => a.GetType() )
                                 .FirstOrDefault( a => a.IsGenericType && a.GetGenericTypeDefinition() == typeof( ConfigurationAttribute<> ) )
                                 ?.GetGenericArguments()[0];
                // Its generated description (TModel.ModelDescription) must exist: a clear error rather than generated
                // code that doesn't compile (the project of the model misses its direct CK.ConfigurationModel reference).
                if( modelType != null && !ConfigurationModelDescription.TryGet( modelType, out _, out var modelError ) )
                {
                    monitor.Error( $"Invalid option '{o.Name}' ('{t.ToCSharpName()}'). {modelError}" );
                    return CSCodeGenerationResult.Failed;
                }
                scope.Append( "}, (sp, configuration) => new " ).Append( t.ToGlobalTypeName() ).Append( "(" );
                bool first = true;
                foreach( var p in o.Constructor.GetParameters() )
                {
                    if( !first ) scope.Append( ", " );
                    first = false;
                    // The option's configuration (null when it is enabled by default or by true), not a service.
                    if( p.ParameterType == typeof( IConfiguration ) )
                    {
                        scope.Append( "(global::Microsoft.Extensions.Configuration.IConfiguration?)configuration" );
                        continue;
                    }
                    if( modelType != null && p.ParameterType == modelType )
                    {
                        scope.Append( "(" ).Append( modelType.ToGlobalTypeName() ).Append( ")configuration!" );
                        continue;
                    }
                    scope.Append( "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<" )
                         .Append( p.ParameterType.ToGlobalTypeName() ).Append( ">( sp )" );
                }
                scope.Append( ")" );
                if( modelType != null ) scope.Append( ", " ).Append( modelType.ToGlobalTypeName() ).Append( ".ModelDescription" );
                scope.Append( " )," ).NewLine();
            }
            scope.Append( "} }," ).NewLine();
            monitor.Info( $"'{owner.ToCSharpName()}' has {island.Count} option(s): '{island.Select( o => o.Option.Name ).Concatenate( "', '" )}'." );
        }
        scope.Append( "} ) {}" ).NewLine();
        return CSCodeGenerationResult.Success;
    }
}
