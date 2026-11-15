using CK.AppIdentity;
using CK.CodeGen;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// Code generator of the <see cref="PartyComponentDirectory"/>: the concrete party components of the BinPath (the
/// concrete classes that implement <see cref="IPartyComponent"/>), their requirements, their abstractions and their order.
/// <list type="bullet">
///   <item>The requirements of a component are the type arguments of its <c>[Requires&lt;...&gt;]</c> attributes. A
///   required type is a party component of the BinPath or an abstraction (an interface that extends
///   <see cref="IPartyComponentAbstraction"/>); anything else is an error.</item>
///   <item>The abstractions of a component are the interfaces that it implements and that extend
///   <see cref="IPartyComponentAbstraction"/>. An abstraction declares its cardinality: <c>[IsSingle]</c> (at most one
///   implementation in a scope) or <c>[IsMultiple]</c> (it can be implemented more than once in a scope); an abstraction
///   that declares none is an error.</item>
///   <item>The order: a required component before the ones that require it, the implementations of a required
///   abstraction before the ones that require it; otherwise by full name. A cycle is an error.</item>
/// </list>
/// </summary>
public sealed class PartyComponentDirectoryImpl : CSCodeGeneratorType
{
    /// <inheritdoc />
    public override CSCodeGenerationResult Implement( IActivityMonitor monitor, Type classType, ICSCodeGenerationContext c, ITypeScope scope )
    {
        var group = c.CurrentRun.ConfigurationGroup;
        // Skips the purely unified BinPath.
        if( group.IsUnifiedPure ) return CSCodeGenerationResult.Success;

        var components = group.TypeSet.Select( t => t.Type )
                                      .Where( t => t.IsClass && !t.IsAbstract && typeof( IPartyComponent ).IsAssignableFrom( t ) )
                                      .OrderBy( t => t.FullName, StringComparer.Ordinal )
                                      .ToList();
        var componentSet = new HashSet<Type>( components );
        bool success = true;
        var requires = new Dictionary<Type, List<Type>>();
        var abstractions = new Dictionary<Type, List<Type>>();
        var multiple = new HashSet<Type>();
        foreach( var t in components )
        {
            var a = t.GetInterfaces().Where( IsAbstraction ).OrderBy( i => i.FullName, StringComparer.Ordinal ).ToList();
            abstractions.Add( t, a );
            foreach( var i in a )
            {
                if( IsMultipleAbstraction( monitor, i, ref success ) ) multiple.Add( i );
            }
            var r = new List<Type>();
            foreach( var attr in t.GetCustomAttributes( inherit: false ) )
            {
                var aType = attr.GetType();
                if( !aType.IsGenericType || aType.Namespace != "CK.Core" || !aType.Name.StartsWith( "RequiresAttribute`", StringComparison.Ordinal ) ) continue;
                foreach( var required in aType.GetGenericArguments() )
                {
                    if( IsAbstraction( required ) || componentSet.Contains( required ) )
                    {
                        if( IsAbstraction( required ) && IsMultipleAbstraction( monitor, required, ref success ) ) multiple.Add( required );
                        if( !r.Contains( required ) ) r.Add( required );
                    }
                    else
                    {
                        monitor.Error( $"The party component '{t:C}' requires '{required:C}' that is neither a party component of the BinPath nor an abstraction (an interface that extends IPartyComponentAbstraction)." );
                        success = false;
                    }
                }
            }
            requires.Add( t, r );
        }
        if( !success ) return CSCodeGenerationResult.Failed;

        var ordered = Sort( monitor, components, requires, abstractions );
        if( ordered == null ) return CSCodeGenerationResult.Failed;

        scope.Append( "public " ).Append( scope.Name )
             .Append( "() : base( new global::CK.AppIdentity.PartyComponentInfo[] {" ).NewLine();
        foreach( var t in ordered )
        {
            scope.Append( "new global::CK.AppIdentity.PartyComponentInfo( typeof(" ).Append( t.ToGlobalTypeName() ).Append( "), new global::System.Type[] {" );
            foreach( var r in requires[t] ) scope.Append( "typeof(" ).Append( r.ToGlobalTypeName() ).Append( ")," );
            scope.Append( "}, new global::System.Type[] {" );
            foreach( var a in abstractions[t] ) scope.Append( "typeof(" ).Append( a.ToGlobalTypeName() ).Append( ")," );
            scope.Append( "} )," ).NewLine();
        }
        scope.Append( "}, new global::System.Collections.Generic.HashSet<global::System.Type>() {" );
        foreach( var m in multiple.OrderBy( m => m.FullName, StringComparer.Ordinal ) ) scope.Append( "typeof(" ).Append( m.ToGlobalTypeName() ).Append( ")," );
        scope.Append( "} ) {}" ).NewLine();
        monitor.Info( $"{ordered.Count} party component(s): '{ordered.Select( t => t.Name ).Concatenate( "', '" )}'." );
        return CSCodeGenerationResult.Success;

        static bool IsAbstraction( Type i ) => i.IsInterface && i != typeof( IPartyComponentAbstraction ) && typeof( IPartyComponentAbstraction ).IsAssignableFrom( i );

        // The cardinality of an abstraction is declared by a mapping attribute ([IsSingle], [IsMultiple]), on it or
        // inherited from the abstractions it extends.
        static bool IsMultipleAbstraction( IActivityMonitor monitor, Type i, ref bool success )
        {
            bool single = false;
            foreach( var t in i.GetInterfaces().Prepend( i ) )
            {
                foreach( var a in t.GetCustomAttributes( inherit: false ) )
                {
                    if( a is IMultipleMappingAttribute ) return true;
                    if( a is ISingleMappingAttribute ) single = true;
                }
            }
            if( !single )
            {
                monitor.Error( $"The party component abstraction '{i:C}' must declare its cardinality: [IsSingle] (at most one implementation in a scope) or [IsMultiple]." );
                success = false;
            }
            return false;
        }
    }

    // Kahn: a component is ready when its required components and all the implementations of its required abstractions
    // are placed. Among the ready ones, the full name gives the order.
    static List<Type>? Sort( IActivityMonitor monitor,
                             List<Type> components,
                             Dictionary<Type, List<Type>> requires,
                             Dictionary<Type, List<Type>> abstractions )
    {
        var before = components.ToDictionary( t => t,
                                              t => requires[t].SelectMany( r => r.IsInterface
                                                                                    ? components.Where( o => o != t && abstractions[o].Contains( r ) )
                                                                                    : [r] )
                                                              .ToHashSet() );
        var result = new List<Type>( components.Count );
        var remaining = new List<Type>( components );
        while( remaining.Count > 0 )
        {
            var ready = remaining.FirstOrDefault( t => before[t].All( result.Contains ) );
            if( ready == null )
            {
                monitor.Error( $"Cycle in the requirements of the party components '{remaining.Select( t => t.ToCSharpName() ).Concatenate( "', '" )}'." );
                return null;
            }
            result.Add( ready );
            remaining.Remove( ready );
        }
        return result;
    }
}
