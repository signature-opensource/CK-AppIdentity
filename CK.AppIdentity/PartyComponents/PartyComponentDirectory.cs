using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The concrete party components of the application (the feature drivers, the client features...), computed by the
/// engine: their requirements (<c>[Requires&lt;T&gt;]</c>), the abstractions they implement
/// (<see cref="IPartyComponentAbstraction"/>) and their order (a required component comes before the components that
/// require it; a required abstraction, after all its implementations). No runtime reflection.
/// </summary>
[ContextBoundDelegation( "CK.Setup.PartyComponentDirectoryImpl, CK.AppIdentity.Engine" )]
public abstract class PartyComponentDirectory : ISingletonAutoService
{
    readonly IReadOnlyList<PartyComponentInfo> _components;
    readonly Dictionary<Type, PartyComponentInfo> _byType;
    readonly IReadOnlySet<Type> _multipleAbstractions;
    readonly IReadOnlyDictionary<string, IReadOnlySet<Type>> _typeSets;

    /// <summary>
    /// Initializes a new directory. Called by the generated code.
    /// </summary>
    /// <param name="components">The components, in their order.</param>
    /// <param name="multipleAbstractions">The abstractions that can be implemented more than once in a scope.</param>
    protected PartyComponentDirectory( IReadOnlyList<PartyComponentInfo> components, IReadOnlySet<Type> multipleAbstractions )
        : this( components, multipleAbstractions, new Dictionary<string, IReadOnlySet<Type>>() )
    {
    }

    /// <summary>
    /// Initializes a new directory with the party components that are members of the type sets of the application (a
    /// local party's set is named after its party name, a client's set after the client's name). Called by the generated code.
    /// </summary>
    /// <param name="components">The components, in their order.</param>
    /// <param name="multipleAbstractions">The abstractions that can be implemented more than once in a scope.</param>
    /// <param name="typeSets">The components that are members of each type set, by the name of the set (case insensitive).</param>
    protected PartyComponentDirectory( IReadOnlyList<PartyComponentInfo> components,
                                       IReadOnlySet<Type> multipleAbstractions,
                                       IReadOnlyDictionary<string, IReadOnlySet<Type>> typeSets )
    {
        _components = components;
        _byType = components.ToDictionary( c => c.ComponentType );
        _multipleAbstractions = multipleAbstractions;
        _typeSets = typeSets.ToDictionary( kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets whether a party component is a member of a type set: the types drive, a component that is not a member of the
    /// set of a party (or of a client) doesn't apply to it. True when there is no such set (an application built without
    /// a "ckonf.json", a test): all the components apply. A set that nothing restricts has all the components.
    /// </summary>
    /// <param name="typeSetName">The name of the set: the party name of a local party, the name of a client.</param>
    /// <param name="componentType">The component type.</param>
    /// <returns>True when the component applies to the party or the client.</returns>
    public bool IsMember( string typeSetName, Type componentType ) => !_typeSets.TryGetValue( typeSetName, out var set ) || set.Contains( componentType );

    /// <summary>
    /// Gets whether this directory knows the members of a type set (see <see cref="IsMember(string, Type)"/>).
    /// </summary>
    /// <param name="typeSetName">The name of the set.</param>
    /// <returns>True when the set is known.</returns>
    public bool HasTypeSet( string typeSetName ) => _typeSets.ContainsKey( typeSetName );

    /// <summary>
    /// Gets the components, in their order.
    /// </summary>
    public IReadOnlyList<PartyComponentInfo> Components => _components;

    /// <summary>
    /// Finds a component by its type.
    /// </summary>
    /// <param name="componentType">The concrete component type.</param>
    /// <returns>The component or null.</returns>
    public PartyComponentInfo? Find( Type componentType ) => _byType.GetValueOrDefault( componentType );

    /// <summary>
    /// Gets whether an abstraction can be implemented more than once in a scope (it is decorated with <c>[IsMultiple]</c>).
    /// </summary>
    /// <param name="abstraction">The abstraction.</param>
    /// <returns>True if it is multiple.</returns>
    public bool IsMultipleAbstraction( Type abstraction ) => _multipleAbstractions.Contains( abstraction );

    /// <summary>
    /// Gets the position of a component in the <see cref="Components"/> (-1 if it is unknown).
    /// </summary>
    /// <param name="componentType">The concrete component type.</param>
    /// <returns>Its index.</returns>
    public int IndexOf( Type componentType ) => _byType.TryGetValue( componentType, out var c ) ? IndexOf( c ) : -1;

    int IndexOf( PartyComponentInfo c )
    {
        for( int i = 0; i < _components.Count; ++i ) if( _components[i] == c ) return i;
        return -1;
    }

    /// <summary>
    /// Checks the requirements of the components of a scope (the features of a client for instance): each required
    /// abstraction is implemented in the scope (or in its outer scope), a unique abstraction is implemented at most once
    /// in the scope, and each required concrete component is in the scope or accepted by <paramref name="isOuterAvailable"/>
    /// (a feature driver allowed by the party of a client for instance).
    /// </summary>
    /// <param name="monitor">The monitor that receives the errors.</param>
    /// <param name="scopeName">The name of the scope, for the errors.</param>
    /// <param name="scope">The concrete component types of the scope.</param>
    /// <param name="isOuterAvailable">Whether a required type is available out of the scope.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool CheckScope( IActivityMonitor monitor, string scopeName, IReadOnlyCollection<Type> scope, Func<Type, bool> isOuterAvailable )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( scope );
        Throw.CheckNotNullArgument( isOuterAvailable );
        bool success = true;
        var infos = new List<PartyComponentInfo>( scope.Count );
        foreach( var t in scope )
        {
            var i = Find( t );
            if( i == null )
            {
                monitor.Error( $"The component '{t:N}' of {scopeName} is not a known party component of the application." );
                success = false;
            }
            else infos.Add( i );
        }
        foreach( var g in infos.SelectMany( i => i.Abstractions.Select( a => (Abstraction: a, Info: i) ) ).GroupBy( x => x.Abstraction ) )
        {
            if( g.Count() > 1 && !IsMultipleAbstraction( g.Key ) )
            {
                monitor.Error( $"{scopeName} has more than one '{g.Key.Name}' ('{g.Select( x => x.Info.ComponentType.Name ).Concatenate( "', '" )}'): it can have at most one." );
                success = false;
            }
        }
        foreach( var i in infos )
        {
            foreach( var r in i.Requires )
            {
                bool inScope = r.IsInterface
                                ? infos.Any( o => o.Abstractions.Contains( r ) )
                                : infos.Any( o => o.ComponentType == r );
                if( !inScope && !isOuterAvailable( r ) )
                {
                    monitor.Error( $"'{i.ComponentType.Name}' of {scopeName} requires '{r.Name}' that {scopeName} doesn't have." );
                    success = false;
                }
            }
        }
        return success;
    }
}
