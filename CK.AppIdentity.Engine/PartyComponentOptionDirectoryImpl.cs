using CK.AppIdentity;
using CK.CodeGen;
using CK.Core;
using CK.Engine.TypeCollector;
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
///   with the <see cref="IPartyComponentOptionAttribute"/> of its kind of owner, with one public constructor.</item>
///   <item>The owners are the concrete party components of the BinPath to which an option applies (its owner type or a
///   type derived from it).</item>
///   <item>The island of an owner: its options are the allowed types, the compatible types are the options of an owner
///   type assignable from it (a relation to another option is ignored).</item>
/// </list>
/// </summary>
public sealed class PartyComponentOptionDirectoryImpl : CSCodeGeneratorType
{
    sealed class OptionModel
    {
        public required ICachedType Type { get; init; }
        public required Type Owner { get; init; }
        public required string Name { get; init; }
        public required bool IsEnabledByDefault { get; init; }
        public required ConstructorInfo Constructor { get; init; }
    }

    /// <inheritdoc />
    public override CSCodeGenerationResult Implement( IActivityMonitor monitor, Type classType, ICSCodeGenerationContext c, ITypeScope scope )
    {
        var group = c.CurrentRun.ConfigurationGroup;
        // Skips the purely unified BinPath.
        if( group.IsUnifiedPure ) return CSCodeGenerationResult.Success;

        if( !TryCollectOptions( monitor, group.TypeSet, out var options ) ) return CSCodeGenerationResult.Failed;
        var owners = group.TypeSet.Select( t => t.Type )
                                  .Where( t => !t.IsAbstract
                                               && !t.IsInterface
                                               && typeof( IPartyComponent ).IsAssignableFrom( t )
                                               && options.Any( o => o.Owner.IsAssignableFrom( t ) ) )
                                  .OrderBy( t => t.FullName, StringComparer.Ordinal )
                                  .ToList();
        bool success = true;
        var islands = new List<(Type Owner, List<(OptionModel Option, List<string> Requires)> Options)>();
        foreach( var owner in owners )
        {
            using( monitor.OpenInfo( $"Ordering the options of '{owner.ToCSharpName()}'." ) )
            {
                var island = TryOrder( monitor, group, owner, options.Where( o => o.Owner.IsAssignableFrom( owner ) ).ToList() );
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
                scope.Append( "new global::CK.AppIdentity.PartyComponentOptionInfo( " )
                     .AppendSourceString( o.Name ).Append( ", typeof(" ).Append( t.ToGlobalTypeName() ).Append( "), " )
                     .Append( o.IsEnabledByDefault ? "true" : "false" ).Append( ", new string[] {" );
                foreach( var r in requires ) scope.AppendSourceString( r ).Append( "," );
                scope.Append( "}, sp => new " ).Append( t.ToGlobalTypeName() ).Append( "(" );
                bool first = true;
                foreach( var p in o.Constructor.GetParameters() )
                {
                    if( !first ) scope.Append( ", " );
                    first = false;
                    scope.Append( "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<" )
                         .Append( p.ParameterType.ToGlobalTypeName() ).Append( ">( sp )" );
                }
                scope.Append( ") )," ).NewLine();
            }
            scope.Append( "} }," ).NewLine();
            monitor.Info( $"'{owner.ToCSharpName()}' has {island.Count} option(s): '{island.Select( o => o.Option.Name ).Concatenate( "', '" )}'." );
        }
        scope.Append( "} ) {}" ).NewLine();
        return CSCodeGenerationResult.Success;
    }

    static bool TryCollectOptions( IActivityMonitor monitor, IReadOnlySet<ICachedType> typeSet, out List<OptionModel> options )
    {
        options = new List<OptionModel>();
        bool success = true;
        foreach( var cached in typeSet )
        {
            var t = cached.Type;
            if( !t.IsClass || t.IsAbstract ) continue;
            var owners = t.GetInterfaces()
                          .Where( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IPartyComponentOption<> ) )
                          .Select( i => i.GetGenericArguments()[0] )
                          .ToList();
            if( owners.Count == 0 ) continue;
            if( owners.Count > 1 )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' is an option of more than one owner type ('{owners.Select( o => o.ToCSharpName() ).Concatenate( "', '" )}'): an option has one owner type." );
                success = false;
                continue;
            }
            var owner = owners[0];
            var attributes = t.GetCustomAttributes( inherit: false ).OfType<IPartyComponentOptionAttribute>().ToList();
            if( attributes.Count != 1 )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' must be decorated with the attribute of its kind of owner: [FeatureOption] for the options of a feature driver, [PartyClientOption] for the options of a party client." );
                success = false;
                continue;
            }
            var a = attributes[0];
            if( !a.OwnerKind.IsAssignableFrom( owner ) )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' is an option of '{owner.ToCSharpName()}' that is not a '{a.OwnerKind.ToCSharpName()}': its attribute [{a.GetType().Name.Replace( "Attribute", "" )}] is the one of another kind of owner." );
                success = false;
                continue;
            }
            var ctors = t.GetConstructors();
            if( ctors.Length != 1 )
            {
                monitor.Error( $"The option '{t.ToCSharpName()}' must have exactly one public constructor (its parameters are resolved from the services of its owner)." );
                success = false;
                continue;
            }
            var name = t.Name.EndsWith( "Option", StringComparison.Ordinal ) && t.Name.Length > 6 ? t.Name[..^6] : t.Name;
            options.Add( new OptionModel { Type = cached, Owner = owner, Name = name, IsEnabledByDefault = a.IsEnabledByDefault, Constructor = ctors[0] } );
        }
        return success;
    }

    // The ResourceSpace island of an owner: its options in their order, with the names of the options they require.
    static List<(OptionModel, List<string>)>? TryOrder( IActivityMonitor monitor, IRunningBinPathGroup group, Type owner, List<OptionModel> island )
    {
        var duplicate = island.GroupBy( o => o.Name, StringComparer.OrdinalIgnoreCase ).FirstOrDefault( g => g.Count() > 1 );
        if( duplicate != null )
        {
            monitor.Error( $"The options '{duplicate.Select( o => o.Type.CSharpName ).Concatenate( "', '" )}' of '{owner.ToCSharpName()}' have the same name '{duplicate.Key}'." );
            return null;
        }
        var byType = island.ToDictionary( o => o.Type );
        var config = new ResSpaceConfiguration( group.TypeCache, byType.ContainsKey )
        {
            PackageResolver = new IslandResolver( owner ),
            RevertOrderingNames = group.EngineConfiguration.RevertOrderingNames
        };
        var byRawType = island.ToDictionary( o => o.Type.Type );
        var requires = island.ToDictionary( o => o, o => new List<string>() );
        bool success = true;
        foreach( var o in island )
        {
            var d = config.RegisterPackage( monitor, o.Type, isOptional: false );
            if( d == null )
            {
                success = false;
                continue;
            }
            // The non optional relations: "only with it", the requiring option can't be enabled without the required one.
            // [Requires<T>] on o: o requires T. [RequiredBy<T>] on o: T requires o.
            // Before the build, the references of a type's relations are their raw types.
            foreach( var r in d.Requires )
            {
                if( !r.IsOptional && r.AsType is { } t && byRawType.TryGetValue( t, out var required ) ) AddRequirement( o, required.Name );
            }
            foreach( var r in d.RequiredBy )
            {
                if( !r.IsOptional && r.AsType is { } t && byRawType.TryGetValue( t, out var requiring ) ) AddRequirement( requiring, o.Name );
            }
        }
        if( !success ) return null;
        var collector = config.Build( monitor );
        if( collector == null ) return null;
        var data = new ResSpaceDataBuilder( collector ).Build( monitor );
        if( data == null ) return null;
        return data.CoreData.Packages.Where( p => p.Type != null && byType.ContainsKey( p.Type ) )
                                     .Select( p => (byType[p.Type!], requires[byType[p.Type!]]) )
                                     .ToList();

        void AddRequirement( OptionModel requiring, string required )
        {
            var list = requires[requiring];
            if( !list.Contains( required ) ) list.Add( required );
        }
    }

    // The compatibility rule of an island: the options of an owner type assignable from the owner. A relation to another
    // option (of another owner) is ignored.
    sealed class IslandResolver : IResPackageDescriptorResolver
    {
        readonly Type _owner;

        public IslandResolver( Type owner ) => _owner = owner;

        public bool IsCompatible( ICachedType type )
            => type.Type.GetInterfaces().Any( i => i.IsGenericType
                                                   && i.GetGenericTypeDefinition() == typeof( IPartyComponentOption<> )
                                                   && i.GetGenericArguments()[0].IsAssignableFrom( _owner ) );

        public bool ResolveRequired( IActivityMonitor monitor, IResPackageDescriptorRegistrar registrar, ResPackageDescriptor.Ref reference )
        {
            monitor.Error( $"The option '{reference}' is required but it is not an option of '{_owner.ToCSharpName()}' in this BinPath." );
            return false;
        }
    }
}
