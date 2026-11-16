using CK.AppIdentity;
using CK.Core;
using CK.TypeCache;
using CK.TypeSet;
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
///   <item>A component is a configurable type (see <see cref="ICachedType.TryGetConfigurableType"/>): a feature driver
///   declares its <see cref="PartyFeatureAttribute"/> (its feature name, its activation and the side of an asymmetric
///   feature: the two sides of a feature agree on their activation), a client feature its <c>PartyClientFeatureAttribute</c>.
///   The configuration names are unique in their scope: among the feature drivers (the keys of a party, "XXXRemote" for
///   the Remote side of an asymmetric feature) and among the client features (the keys of a client).</item>
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
        var names = new Dictionary<Type, (string Name, string Key, TypeActivation Activation, FeatureSide Side)>();
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
            if( !c.TryGetConfigurableType( monitor, out var configurable ) )
            {
                success = false;
                continue;
            }
            bool isDriver = typeof( IApplicationIdentityFeatureDriver ).IsAssignableFrom( t );
            if( configurable == null )
            {
                monitor.Error( isDriver
                                ? $"The feature driver '{t:C}' must declare its feature: [PartyFeature( \"Name\", TypeActivation.Optional )] for instance."
                                : $"The client feature '{t:C}' must declare its name: [PartyClientFeature( \"Name\", TypeActivation.Optional )] for instance." );
                success = false;
                continue;
            }
            var feature = configurable as PartyFeatureAttribute;
            if( isDriver != (feature != null) )
            {
                monitor.Error( isDriver
                                ? $"The feature driver '{t:C}' must be decorated with [PartyFeature] (not [{configurable.GetType().ToCSharpName()}])."
                                : $"The client feature '{t:C}' cannot be decorated with [PartyFeature]: it is not a feature driver." );
                success = false;
                continue;
            }
            // The feature name of a side is the name of the feature (the configuration name of the Remote side is "XXXRemote").
            var side = feature?.Side ?? FeatureSide.None;
            names.Add( t, (feature?.FeatureName ?? configurable.Name, configurable.Name, configurable.Activation, side) );
        }
        if( !success ) return null;
        // The scopes of the names: the keys of a party (the feature drivers) and the keys of a client (the client features).
        foreach( var scope in names.GroupBy( kv => typeof( IApplicationIdentityFeatureDriver ).IsAssignableFrom( kv.Key ) ) )
        {
            foreach( var g in scope.GroupBy( kv => kv.Value.Key, StringComparer.OrdinalIgnoreCase ).Where( g => g.Count() > 1 ) )
            {
                monitor.Error( $"The {(scope.Key ? "feature drivers" : "client features")} '{g.Select( kv => kv.Key.ToCSharpName() ).Order().Concatenate( "', '" )}' have the same name '{g.Key}': a name is unique among the {(scope.Key ? "features of a party" : "features of a client")}." );
                success = false;
            }
        }
        foreach( var g in names.Where( kv => kv.Value.Side != FeatureSide.None ).GroupBy( kv => kv.Value.Name, StringComparer.OrdinalIgnoreCase ) )
        {
            if( g.Select( kv => kv.Value.Activation ).Distinct().Count() > 1 )
            {
                monitor.Error( $"The two sides of the asymmetric feature '{g.Key}' ('{g.Select( kv => kv.Key.ToCSharpName() ).Concatenate( "', '" )}') must agree on their activation." );
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
                                                                 names[t].Activation,
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
