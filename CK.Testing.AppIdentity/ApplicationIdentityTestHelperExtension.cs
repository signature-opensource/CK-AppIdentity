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
        public sealed class RunningAppIdentity : IAsyncDisposable
        {
            readonly ApplicationIdentityService _s;
            readonly ServiceProvider _provider;

            internal RunningAppIdentity( ApplicationIdentityService s, ServiceProvider provider )
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
        /// Creates a <see cref="RunningAppIdentity"/> that must be disposed once done with it.
        /// <list type="bullet">
        ///   <item>The configuration is always registered as a singleton in the services.</item>
        ///   <item>
        ///     When <paramref name="configureServices"/> is null, only the <see cref="ApplicationIdentityService"/>,
        ///     the <see cref="IActivityMonitor"/> and <see cref="IParallelLogger"/> are registered.
        ///   </item>
        ///   <item>
        ///     When a non null <paramref name="configureServices"/> is provided it must register all the services,
        ///     including the required <see cref="ApplicationIdentityService"/>.
        ///     <para>
        ///     Beware of IHostedService registration (when not using automatic DI). It must be:
        ///     <code>
        ///     services.AddSingleton&lt;ApplicationIdentityService&gt;();
        ///     services.AddSingleton&lt;IHostedService&gt;(sp =&gt; sp.GetRequiredService&lt;ApplicationIdentityService&gt;() );
        ///     </code>
        ///     </para>
        ///   </item>
        /// </list>
        /// <para>
        /// All existing <see cref="IHostedService"/> are started.
        /// </para>
        /// </summary>
        /// <param name="helper">This test helper.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="configureServices">Null for minimal support otherwise the full configuration of services.</param>
        /// <param name="useTestAppIdentityStore">
        /// By default, preconfigures the <paramref name="configuration"/>["StoreRootPath"] to
        /// be "<see cref="IBasicTestHelper.TestProjectFolder"/>/CK-AppIdentity-Store".
        /// </param>
        /// <returns>The running context.</returns>
        public static Task<RunningAppIdentity> CreateRunningAppIdentityServiceAsync( this IMonitorTestHelper helper,
                                                                                     Action<MutableConfigurationSection> configuration,
                                                                                     Action<IServiceCollection>? configureServices = null,
                                                                                     bool useTestAppIdentityStore = true )
        {
            Throw.CheckNotNullArgument( configuration );
            if( useTestAppIdentityStore )
            {
                Throw.DebugAssert( nameof( ApplicationIdentityServiceConfiguration.StoreRootPath ) == "StoreRootPath" );
                var prev = configuration;
                configuration = c =>
                {
                    c["StoreRootPath"] = helper.TestProjectFolder.AppendPart( "CK-AppIdentity-Store" );
                    prev( c );
                };
            }
            var c = ApplicationIdentityServiceConfiguration.Create( helper.Monitor, configuration );
            Throw.DebugAssert( c != null );
            return CreateRunningAppIdentityServiceAsync( helper, c, configureServices );
        }

        /// <summary>
        /// Creates a <see cref="RunningAppIdentity"/> that must be disposed once done with it.
        /// <list type="bullet">
        ///   <item>The configuration is always registered as a singleton in the services.</item>
        ///   <item>
        ///     When <paramref name="configureServices"/> is null, only the <see cref="ApplicationIdentityService"/>,
        ///     the <see cref="IActivityMonitor"/> and <see cref="IParallelLogger"/> are registered.
        ///   </item>
        ///   <item>
        ///     When a non null <paramref name="configureServices"/> is provided it must register all the services,
        ///     including the required <see cref="ApplicationIdentityService"/>.
        ///     <para>
        ///     Beware of IHostedService registration (when not using automatic DI). It must be:
        ///     <code>
        ///     services.AddSingleton&lt;ApplicationIdentityService&gt;();
        ///     services.AddSingleton&lt;IHostedService&gt;(sp =&gt; sp.GetRequiredService&lt;ApplicationIdentityService&gt;() );
        ///     </code>
        ///     </para>
        ///   </item>
        /// </list>
        /// <para>
        /// All existing <see cref="IHostedService"/> are started.
        /// </para>
        /// </summary>
        /// <param name="helper">This test helper.</param>
        /// <param name="c">The configuration.</param>
        /// <param name="configureServices">Null for minimal support otherwise the full configuration of services.</param>
        /// <returns>The running context.</returns>
        public static async Task<RunningAppIdentity> CreateRunningAppIdentityServiceAsync( this IBasicTestHelper helper,
                                                                                           ApplicationIdentityServiceConfiguration c,
                                                                                           Action<IServiceCollection>? configureServices = null )
        {
            var serviceBuilder = new ServiceCollection();
            serviceBuilder.AddSingleton( c );
            if( configureServices == null )
            {
                // Minimal configuration.
                serviceBuilder.AddSingleton<ApplicationIdentityService>();
                serviceBuilder.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
                serviceBuilder.AddScoped<IActivityMonitor, ActivityMonitor>();
                serviceBuilder.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
            }
            else
            {
                configureServices( serviceBuilder );
            }
            var services = serviceBuilder.BuildServiceProvider();
            var s = services.GetRequiredService<ApplicationIdentityService>();
            // This is done by host.
            foreach( var service in services.GetServices<IHostedService>() )
            {
                await service.StartAsync( default );
            }
            // We wait for the FeatureBuildersInitialization task.
            await s.InitializationTask;
            return new RunningAppIdentity( s, services );
        }
    }
}
