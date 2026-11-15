using CK.AppIdentity;
using CK.Core;
using CK.Engine.TypeCollector;
using CK.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Testing;

/// <summary>
/// Assembles an <see cref="ApplicationIdentityService"/> without the engine, for the tests of the run time. In an
/// application, the engine generates the <see cref="PartyComponentDirectory"/> (the party components, their feature names,
/// their optionality...): here it is computed from the component types by the same analysis (<see cref="PartyComponentAnalysis"/>).
/// </summary>
public static class AppIdentityTestHelper
{
    sealed class TestPartyComponentDirectory : PartyComponentDirectory
    {
        public TestPartyComponentDirectory( IReadOnlyList<PartyComponentInfo> components, IReadOnlySet<Type> multipleAbstractions )
            : base( components, multipleAbstractions )
        {
        }
    }

    /// <summary>
    /// Creates the <see cref="PartyComponentDirectory"/> of party component types (the feature drivers, the client
    /// features...), as the engine computes it. Throws on error (see the logs).
    /// </summary>
    /// <param name="components">The party component types.</param>
    /// <returns>The directory.</returns>
    public static PartyComponentDirectory CreateComponentDirectory( IEnumerable<Type> components )
    {
        var cache = new GlobalTypeCache();
        var result = PartyComponentAnalysis.Analyze( TestHelper.Monitor, components.Distinct().Select( cache.Get ) );
        Throw.CheckState( "Invalid party components (see the logs).", result != null );
        return new TestPartyComponentDirectory( result.Components, result.MultipleAbstractions );
    }

    /// <summary>
    /// Creates the <see cref="PartyComponentDirectory"/> of party component types (see <see cref="CreateComponentDirectory(IEnumerable{Type})"/>).
    /// </summary>
    /// <param name="components">The party component types.</param>
    /// <returns>The directory.</returns>
    public static PartyComponentDirectory CreateComponentDirectory( params Type[] components ) => CreateComponentDirectory( (IEnumerable<Type>)components );

    /// <summary>
    /// Registers what the engine registers in an application: the configuration, the <see cref="ApplicationIdentityService"/>
    /// (and its <see cref="IHostedService"/>), the feature drivers in their order (their dependency order in an
    /// application) and the <see cref="PartyComponentDirectory"/> of the drivers and of the other components.
    /// </summary>
    /// <param name="services">These services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="drivers">The feature driver types (<see cref="ApplicationIdentityFeatureDriver"/>), in their order.</param>
    /// <param name="otherComponents">The other party components of the directory (the client features...).</param>
    /// <returns>These services.</returns>
    public static IServiceCollection AddApplicationIdentityForTests( this IServiceCollection services,
                                                                     ApplicationIdentityServiceConfiguration configuration,
                                                                     IEnumerable<Type> drivers,
                                                                     IEnumerable<Type>? otherComponents = null )
    {
        var d = drivers.ToList();
        services.AddSingleton( configuration );
        services.AddSingleton<ApplicationIdentityService>();
        services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        foreach( var t in d )
        {
            services.AddSingleton( t );
            services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( t ) );
        }
        services.AddSingleton( CreateComponentDirectory( d.Concat( otherComponents ?? [] ) ) );
        return services;
    }

    /// <summary>
    /// Registers the <see cref="PartyComponentDirectory"/> of the feature drivers already registered in these services (the
    /// concrete <see cref="ApplicationIdentityFeatureDriver"/> service types) and of other components (the client features,
    /// a driver that is required but not registered...). Call it once the drivers are registered.
    /// </summary>
    /// <param name="services">These services.</param>
    /// <param name="otherComponents">The other party components of the directory.</param>
    /// <returns>These services.</returns>
    public static IServiceCollection AddPartyComponentDirectory( this IServiceCollection services, params Type[] otherComponents )
    {
        var drivers = services.Select( d => d.ServiceType )
                              .Where( t => t.IsClass && !t.IsAbstract && typeof( ApplicationIdentityFeatureDriver ).IsAssignableFrom( t ) );
        services.AddSingleton( CreateComponentDirectory( drivers.Concat( otherComponents ) ) );
        return services;
    }

    sealed class TestPartyComponentOptionDirectory : PartyComponentOptionDirectory
    {
        public TestPartyComponentOptionDirectory( IReadOnlyDictionary<Type, IReadOnlyList<PartyComponentOptionInfo>> options )
            : base( options )
        {
        }
    }

    /// <summary>
    /// Registers a <see cref="PartyComponentOptionDirectory"/> of option types (<see cref="IPartyComponentOption{TOwner}"/>),
    /// as the engine generates it, but simpler: the options of an owner are in the given order (their topology attributes
    /// are ignored, they have no requirements) and they are the options of their declared owner type only (not of the types
    /// derived from it). Their name and whether they are enabled by default come from their attribute; they are created by
    /// reflection: the <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> parameter of their constructor is
    /// their configuration, the other ones are services.
    /// </summary>
    /// <param name="services">These services.</param>
    /// <param name="options">The option types, in their order.</param>
    /// <returns>These services.</returns>
    public static IServiceCollection AddPartyComponentOptions( this IServiceCollection services, params Type[] options )
    {
        var byOwner = new Dictionary<Type, List<PartyComponentOptionInfo>>();
        foreach( var t in options )
        {
            var owner = t.GetInterfaces().Single( i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof( IPartyComponentOption<> ) )
                         .GetGenericArguments()[0];
            var a = t.GetCustomAttributes( inherit: false ).OfType<IPartyComponentOptionAttribute>().Single();
            var ctor = t.GetConstructors().Single();
            var name = t.Name.EndsWith( "Option", StringComparison.Ordinal ) && t.Name.Length > 6 ? t.Name[..^6] : t.Name;
            var info = new PartyComponentOptionInfo( name, t, a.IsEnabledByDefault, [], ( sp, configuration ) =>
            {
                var args = ctor.GetParameters()
                               .Select( p => p.ParameterType == typeof( Microsoft.Extensions.Configuration.IConfiguration )
                                                ? configuration
                                                : sp.GetRequiredService( p.ParameterType ) )
                               .ToArray();
                try
                {
                    return ctor.Invoke( args );
                }
                catch( System.Reflection.TargetInvocationException ex ) when( ex.InnerException != null )
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw( ex.InnerException );
                    throw;
                }
            } );
            if( !byOwner.TryGetValue( owner, out var list ) ) byOwner.Add( owner, list = new List<PartyComponentOptionInfo>() );
            list.Add( info );
        }
        services.AddSingleton<PartyComponentOptionDirectory>( new TestPartyComponentOptionDirectory( byOwner.ToDictionary( kv => kv.Key, kv => (IReadOnlyList<PartyComponentOptionInfo>)kv.Value ) ) );
        return services;
    }

    /// <summary>
    /// Creates a service provider with the <see cref="PartyComponentDirectory"/> of party component types: for a test
    /// that creates its <see cref="ApplicationIdentityService"/> and its drivers itself.
    /// </summary>
    /// <param name="components">The party component types.</param>
    /// <returns>The service provider.</returns>
    public static ServiceProvider CreateServiceProvider( params Type[] components )
    {
        var services = new ServiceCollection();
        services.AddSingleton( CreateComponentDirectory( components ) );
        return services.BuildServiceProvider();
    }
}
