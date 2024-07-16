using CK.AppIdentity;
using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Adds extension methods on <see cref="IHostApplicationBuilder"/>.
    /// </summary>
    public static class CKAppIdentityHostApplicationBuilderExtensions
    {
        /// <summary>
        /// Initializes a <see cref="ApplicationIdentityServiceConfiguration"/> from "CK-AppIdentity" configuration section.
        /// The configured instance <see cref="ApplicationIdentityServiceConfiguration"/> is added to the <see cref="IHostApplicationBuilder.Services"/>
        /// as a singleton service and <see cref="CoreApplicationIdentity"/> is initialized if possible.
        /// <para>
        /// This cannot be called multiple times: a <see cref="InvalidOperationException"/> is thrown.
        /// This differ from <see cref="HostApplicationBuilderMonitoringExtensions.UseCKMonitoring{T}(T)"/> that can reconfigure
        /// the <see cref="CK.Monitoring.GrandOutput.Default"/>. This builds and register a <see cref="ApplicationIdentityServiceConfiguration"/>
        /// based on the current <see cref="IHostApplicationBuilder.Configuration"/> once and only once.
        /// </para>
        /// </summary>
        /// <param name="builder">This application builder</param>
        /// <param name="contextDescriptor">Defaults to <see cref="Environment.CommandLine"/>.</param>
        /// <returns>This builder.</returns>
        public static T AddApplicationIdentityServiceConfiguration<T>( this T builder, string? contextDescriptor = null ) where T : IHostApplicationBuilder
        {
            var tOnce = typeof( ApplicationIdentityServiceConfiguration );
            if( builder.Properties.ContainsKey( tOnce ) )
            {
                Throw.InvalidOperationException( "UseCKAppIdentity must be called only once." );
            }
            builder.Properties.Add( tOnce, tOnce );
            var monitor = builder.GetBuilderMonitor();
            var config = ApplicationIdentityServiceConfiguration.Create( monitor, builder.Environment, builder.Configuration.GetSection( "CK-AppIdentity" ) );
            if( config != null )
            {

                if( CoreApplicationIdentity.TryConfigure( identity =>
                {
                    identity.DomainName = config.DomainName;
                    identity.PartyName = config.PartyName;
                    identity.EnvironmentName = config.EnvironmentName;
                    identity.ContextDescriptor = contextDescriptor ?? Environment.CommandLine;
                } ) )
                {
                    CoreApplicationIdentity.Initialize();
                    monitor.Trace( $"CoreApplicationIdentity initialized: '{CoreApplicationIdentity.Instance.FullName}'." );
                }
                else
                {
                    monitor.Warn( $"Unable to configure CoreApplicationIdentity since it is already initialized: '{CoreApplicationIdentity.Instance.FullName}'." );
                }
                builder.Services.AddSingleton( config );
            }
            return builder;
        }
    }
}
