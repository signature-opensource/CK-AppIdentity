using CK.AppIdentity;
using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Adds extension methods on the <see cref="HostApplicationBuilder"/> of a host.
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
    /// <param name="builder">The builder of the host.</param>
    /// <param name="contextDescriptor">
    /// Defaults to the path of the application (the first command line argument): never the whole command line, whose
    /// arguments may carry secrets (the descriptor is logged and exported with the logs). It describes the running context,
    /// not the running instance (that is <see cref="CoreApplicationIdentity.InstanceId"/>): it is stable across restarts.
    /// </param>
    /// <returns>This builder.</returns>
    public static HostApplicationBuilder AddApplicationIdentityServiceConfiguration( this HostApplicationBuilder builder, string? contextDescriptor = null )
    {
        // The default ContextDescriptor identifies the process without its arguments (they may carry secrets).
        contextDescriptor ??= Environment.GetCommandLineArgs()[0];

        var uniqueKey = typeof( ApplicationIdentityServiceConfiguration );
        var properties = ((IHostApplicationBuilder)builder).Properties;
        if( properties.TryGetValue( uniqueKey, out var existingCtx ) )
        {
            // Useless if CoreApplicationIdentity is already initialized.
            if( !CoreApplicationIdentity.IsInitialized )
            {
                var exists = (string)existingCtx;
                if( exists != contextDescriptor )
                {
                    builder.GetBuilderMonitor().Info( $"Change identity ContextDescriptor from '{exists}' to '{contextDescriptor}'." );
                }
                properties[uniqueKey] = contextDescriptor;
            }
            return builder;
        }
        properties.Add( uniqueKey, contextDescriptor );
        builder.AddAutoConfigure( DoConfigure );
        return builder;

        static void DoConfigure( IActivityMonitor monitor, HostApplicationBuilder builder )
        {
            var config = ApplicationIdentityServiceConfiguration.Create( monitor, builder.Environment, builder.Configuration.GetSection( "CK-AppIdentity" ) );
            if( config != null )
            {
                string contextDescriptor = (string)((IHostApplicationBuilder)builder).Properties[typeof( ApplicationIdentityServiceConfiguration )];
                if( CoreApplicationIdentity.TryConfigure( identity =>
                {
                    identity.DomainName = config.DomainName;
                    identity.PartyName = config.PartyName;
                    identity.EnvironmentName = config.EnvironmentName;
                    identity.ContextDescriptor = contextDescriptor;
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
        }
    }
}
