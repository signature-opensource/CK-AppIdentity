using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace CK.AppIdentity
{
    /// <summary>
    /// Extends <see cref="IServiceProvider"/>.
    /// </summary>
    public static class CKAppIdentityServiceProviderExtensions
    {
        /// <summary>
        /// Simple helper that obtains the <see cref="ApplicationIdentityService"/> and calls
        /// <see cref="ApplicationIdentityService.StartAndInitializeAsync"/>. This can be called
        /// right after the <see cref="IServiceProvider"/> is built.
        /// <para>
        /// This short-cuts the ApplicationIdentityService's <see cref="IHostedService"/> implementation and
        /// initializes all the features: other services - including hosted ones - that depends on features
        /// can safely be resolved.
        /// </para>
        /// </summary>
        /// <param name="services">This provider.</param>
        /// <returns>The <see cref="ApplicationIdentityService.InitializationTask"/>.</returns>
        public static Task StartAndInitializeApplicationIdentityServiceAsync( this IServiceProvider services )
        {
            var s = services.GetRequiredService<ApplicationIdentityService>();
            return s.StartAndInitializeAsync();
        }
    }
}
