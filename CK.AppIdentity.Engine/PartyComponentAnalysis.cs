using CK.AppIdentity;
using CK.Core;
using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CK.Setup;

/// <summary>
/// Computes the <see cref="PartyComponentDirectory"/> content from types: the concrete party components (the concrete
/// classes that implement <see cref="IPartyComponent"/>), their requirements, their abstractions, their order, their
/// feature names, their optionality and the side of an asymmetric feature. The generated directory (see
/// <see cref="PartyComponentDirectoryImpl"/>) is written from it; a test that assembles its services without the engine
/// builds its directory with it (CK.Testing.AppIdentity).
/// <list type="bullet">
///   <item>The requirements of a component are the type arguments of its <c>[Requires&lt;...&gt;]</c> attributes. A
///   required type is a party component of the types or an abstraction (an interface that extends
///   <see cref="IPartyComponentAbstraction"/>); anything else is an error.</item>
///   <item>The abstractions of a component are the interfaces that it implements and that extend
///   <see cref="IPartyComponentAbstraction"/>. An abstraction declares its cardinality: <c>[IsSingle]</c> (at most one
///   implementation in a scope) or <c>[IsMultiple]</c> (it can be implemented more than once in a scope); an abstraction
///   that declares none is an error.</item>
///   <item>The order: a required component before the ones that require it, the implementations of a required
///   abstraction before the ones that require it; otherwise by full name. A cycle is an error.</item>
///   <item>The feature name and the optionality follow the rules of the packages (see <see cref="FeaturePackages"/>);
///   the side of an asymmetric feature is the one of its <see cref="PartyFeatureAttribute"/> (it requires an explicit
///   name, and the two sides of a feature agree on their optionality).</item>
/// </list>
/// </summary>
public static class PartyComponentAnalysis
{
    /// <summary>
    /// The content of a <see cref="PartyComponentDirectory"/>.
    /// </summary>
    /// <param name="Components">The ordered components.</param>
    /// <param name="MultipleAbstractions">The abstractions that can be implemented more than once in a scope.</param>
    public sealed record Result( IReadOnlyList<PartyComponentInfo> Components, IReadOnlySet<Type> MultipleAbstractions );

    /// <summary>
    /// Analyzes the party components among types.
    /// </summary>
    /// <param name="monitor">The monitor that logs the errors.</param>
    /// <param name="types">The types to consider (only the concrete party components are kept).</param>
    /// <returns>The result or null on error.</returns>
    public static Result? Analyze( IActivityMonitor monitor, IEnumerable<ICachedType> types )
    {
        var cached = types.Where( t => t.Type.IsClass && !t.Type.IsAbstract && typeof( IPartyComponent ).IsAssignableFrom( t.Type ) )
                          .OrderBy( t => t.Type.FullName, StringComparer.Ordinal )
                          .ToList();
        var components = cached.Select( t => t.Type ).ToList();
        var componentSet = new HashSet<Type>( components );
        bool success = true;
        var requires = new Dictionary<Type, List<Type>>();
        var abstractions = new Dictionary<Type, List<Type>>();
        var names = new Dictionary<Type, (string? Name, bool IsOptional, FeatureSide Side)>();
        var multiple = new HashSet<Type>();
        foreach( var c in cached )
        {
            var t = c.Type;
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
                        monitor.Error( $"The party component '{t:C}' requires '{required:C}' that is neither a party component nor an abstraction (an interface that extends IPartyComponentAbstraction)." );
                        success = false;
                    }
                }
            }
            requires.Add( t, r );
            if( !FeaturePackages.TryGetFeatureName( monitor, c, out var packageName, out _ ) )
            {
                success = false;
                continue;
            }
            var feature = FindPartyFeature( t );
            var side = feature?.Side ?? FeatureSide.None;
            if( side != FeatureSide.None && feature!.FeatureName == null )
            {
                monitor.Error( $"The feature driver '{t:C}' is the {side} side of an asymmetric feature: its [PartyFeature] must give the feature name." );
                success = false;
            }
            // The feature name of a side is the name of the feature (the package name of the Remote side is "XXXRemote").
            var featureName = side != FeatureSide.None ? feature!.FeatureName : packageName;
            names.Add( t, (featureName, FeaturePackages.IsOptional( c ), side) );
        }
        if( !success ) return null;
        foreach( var g in names.Where( kv => kv.Value.Side != FeatureSide.None ).GroupBy( kv => kv.Value.Name, StringComparer.OrdinalIgnoreCase ) )
        {
            if( g.Select( kv => kv.Value.IsOptional ).Distinct().Count() > 1 )
            {
                monitor.Error( $"The two sides of the asymmetric feature '{g.Key}' ('{g.Select( kv => kv.Key.ToCSharpName() ).Concatenate( "', '" )}') must agree on their optionality." );
                success = false;
            }
        }
        if( !success ) return null;
        var ordered = Sort( monitor, components, requires, abstractions );
        if( ordered == null ) return null;
        var infos = ordered.Select( t => new PartyComponentInfo( t,
                                                                 requires[t],
                                                                 abstractions[t],
                                                                 names[t].Name,
                                                                 names[t].IsOptional,
                                                                 names[t].Side ) ).ToList();
        return new Result( infos, multiple );

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

        // The closest [PartyFeature] in the base classes (the one that names the feature).
        static PartyFeatureAttribute? FindPartyFeature( Type t )
        {
            for( var b = t; b != null; b = b.BaseType )
            {
                var a = b.GetCustomAttribute<PartyFeatureAttribute>( inherit: false );
                if( a != null ) return a;
            }
            return null;
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
