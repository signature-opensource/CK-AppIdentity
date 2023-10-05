using CK.AppIdentity;
using CK.Core;
using CK.Testing;
using FluentAssertions.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace CK
{
    /// <summary>
    /// Provides simple helpers to <see cref="IMonitorTestHelper"/>.
    /// </summary>
    public static class ApplicationIdentityTestHelperExtension
    {
        /// <summary>
        /// Minimal application context with started <see cref="IHostedService"/> and 
        /// a completed <see cref="ApplicationIdentityService.InitializationTask"/>.
        /// </summary>
        public sealed class MinimalRunningContext : IAsyncDisposable
        {
            readonly ApplicationIdentityService _s;
            readonly ServiceProvider _provider;

            internal MinimalRunningContext( ApplicationIdentityService s, ServiceProvider provider )
            {
                _s = s;
                _provider = provider;
            }

            /// <summary>
            /// Gets the root service context.
            /// </summary>
            public IServiceProvider Services => _provider;

            /// <summary>
            /// Gets the identity service.
            /// </summary>
            public ApplicationIdentityService ApplicationIdentityService => _s;

            /// <summary>
            /// Stops all <see cref="IHostedService"/> and disposes the services.
            /// </summary>
            /// <returns>The awaitable.</returns>
            public async ValueTask DisposeAsync()
            {
                foreach( var service in _provider.GetServices<IHostedService>() )
                {
                    await service.StopAsync( default );
                }
                await _provider.DisposeAsync();
            }
        }

        /// <summary>
        /// Creates a <see cref="MinimalRunningContext"/> that must be disposed once done with it.
        /// <para>
        /// This creates a minimal application context, with a registered <see cref="IActivityMonitor"/>
        /// and <see cref="IParallelLogger"/> and all the <see cref="IHostedService"/> started.
        /// </para>
        /// </summary>
        /// <param name="helper">This test helper.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="configureServices">
        /// Optional services (other than the identity service and the activity monitor)
        /// to configure. <see cref="ApplicationIdentityFeatureDriver"/> must be explictly registered (as singletons).
        /// </param>
        /// <param name="useTestAppIdentityStore">
        /// By default, preconfigures the <paramref name="configuration"/>["StoreRootPath"] to
        /// be "<see cref="IBasicTestHelper.TestProjectFolder"/>/CK-AppIdentity-Store".
        /// </param>
        /// <returns>The started service.</returns>
        public static Task<MinimalRunningContext> CreateApplicationServiceAsync( this IMonitorTestHelper helper,
                                                                                 Action<MutableConfigurationSection> configuration,
                                                                                 Action<IServiceCollection>? configureServices = null,
                                                                                 bool useTestAppIdentityStore = true )
        {
            if( useTestAppIdentityStore )
            {
                Throw.DebugAssert( nameof( ApplicationIdentityServiceConfiguration.StoreRootPath ) == "StoreRootPath" );
                configuration = c =>
                {
                    c["StoreRootPath"] = helper.TestProjectFolder.AppendPart( "CK-AppIdentity-Store" );
                    configuration( c );
                };
            }
            var c = ApplicationIdentityServiceConfiguration.Create( helper.Monitor, configuration );
            Throw.DebugAssert( c != null );
            return CreateApplicationServiceAsync( helper, c, configureServices );
        }

        /// <summary>
        /// Creates a <see cref="MinimalRunningContext"/> that must be disposed once done with it.
        /// <para>
        /// This creates a minimal application context, with a registered <see cref="IActivityMonitor"/>
        /// and <see cref="IParallelLogger"/> and all the <see cref="IHostedService"/> started.
        /// </para>
        /// </summary>
        /// <param name="helper">This test helper.</param>
        /// <param name="c">The configuration.</param>
        /// <param name="configureServices">
        /// Optional services (other than the identity service and the activity monitor)
        /// to configure. <see cref="ApplicationIdentityFeatureDriver"/> must be explictly registered (as singletons).
        /// </param>
        /// <returns>The started service.</returns>
        public static async Task<MinimalRunningContext> CreateApplicationServiceAsync( this IBasicTestHelper helper,
                                                                                       ApplicationIdentityServiceConfiguration c,
                                                                                       Action<IServiceCollection>? configureServices = null )
        {
            var serviceBuilder = new ServiceCollection();
            serviceBuilder.AddSingleton( c );
            serviceBuilder.AddSingleton<ApplicationIdentityService>();
            // Don't UseCKMonitoring here or the GrandOutput.Default will be reconfigured:
            // only register the IActivityMonitor and its ParallelLogger.
            serviceBuilder.AddScoped<IActivityMonitor, ActivityMonitor>();
            serviceBuilder.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
            configureServices?.Invoke( serviceBuilder );
            var services = serviceBuilder.BuildServiceProvider();
            var s = services.GetRequiredService<ApplicationIdentityService>();
            // This is done by host.
            foreach( var service in services.GetServices<IHostedService>() )
            {
                await service.StartAsync( default );
            }
            // We wait for the FeatureBuildersInitialization task.
            await s.InitializationTask;
            return new MinimalRunningContext( s, services );
        }
    }
}
