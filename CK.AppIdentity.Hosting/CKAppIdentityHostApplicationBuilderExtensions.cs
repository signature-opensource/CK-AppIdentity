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
        /// The configured instance <see cref="ApplicationIdentityServiceConfiguration"/> will be added to the <see cref="IHostApplicationBuilder.Services"/>
        /// as a singleton service and <see cref="CoreApplicationIdentity"/> will be initialized if possible by <see cref="HostApplicationBuilderMonitoringExtensions.ApplyAutoConfigure"/>.
        /// <para>
        /// This can called multiple times: the last <paramref name="contextDescriptor"/> wins.
        /// </para>
        /// </summary>
        /// <param name="builder">This application builder</param>
        /// <param name="contextDescriptor">Defaults to <see cref="Environment.CommandLine"/>.</param>
        /// <returns>This builder.</returns>
        public static T AddApplicationIdentityServiceConfiguration<T>( this T builder, string? contextDescriptor = null ) where T : IHostApplicationBuilder
        {
            // The default ContextDescriptor is the command line.
            contextDescriptor ??= Environment.CommandLine;

            var uniqueKey = typeof( ApplicationIdentityServiceConfiguration );
            if( builder.Properties.TryGetValue( uniqueKey, out var existingCtx ) )
            {
                // Useless if CoreApplicationIdentity is already initialized.
                if( !CoreApplicationIdentity.IsInitialized )
                {
                    var exists = (string)existingCtx;
                    if( exists != contextDescriptor )
                    {
                        builder.GetBuilderMonitor().Info( $"Change identity ContextDescriptor from '{exists}' to '{contextDescriptor}'." );
                    }
                    builder.Properties[uniqueKey] = contextDescriptor;
                }
                return builder;
            }
            builder.Properties.Add( uniqueKey, contextDescriptor );
            builder.AddAutoConfigureAAAAAA( ( monitor, builder ) =>
            {
                var config = ApplicationIdentityServiceConfiguration.Create( monitor, builder.Environment, builder.Configuration.GetSection( "CK-AppIdentity" ) );
                if( config != null )
                {
                    if( CoreApplicationIdentity.TryConfigure( identity =>
                    {
                        identity.DomainName = config.DomainName;
                        identity.PartyName = config.PartyName;
                        identity.EnvironmentName = config.EnvironmentName;
                        identity.ContextDescriptor = (string)builder.Properties[typeof( ApplicationIdentityServiceConfiguration )];
                    } ) )
                    {
                        CoreApplicationIdentity.Initialize();
                        monitor.Trace( $"CoreApplicationIdentity initialized: '{CoreApplicationIdentity.Instance.FullName}' (ContextDescriptor: '{contextDescriptor}')." );
                    }
                    else
                    {
                        monitor.Warn( $"Unable to configure CoreApplicationIdentity since it is already initialized: '{CoreApplicationIdentity.Instance.FullName}'." );
                    }
                    builder.Services.AddSingleton( config );
                } 
            } );
            return builder;
        }
    }
}
