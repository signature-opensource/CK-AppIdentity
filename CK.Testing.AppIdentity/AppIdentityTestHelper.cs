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
