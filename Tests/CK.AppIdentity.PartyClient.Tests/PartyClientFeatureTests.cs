using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.PartyClientTests;

[TestFixture]
public class PartyClientFeatureTests
{
    /// <summary>
    /// An opt-in feature that the clients of the <see cref="ServedClientFactory"/> require.
    /// </summary>
    public sealed class ServingFeatureDriver : ApplicationIdentityFeatureDriver
    {
        public ServingFeatureDriver( ApplicationIdentityService s ) : base( s, isAllowedByDefault: false ) { }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );

        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    /// <summary>
    /// A served client: announced, with a request path derived from its name, and a "Color" fact.
    /// </summary>
    public sealed class ServedClient : PartyClient
    {
        public ServedClient( ILocalParty party, string name, ImmutableConfigurationSection configuration, string? color )
            : base( party, name, configuration )
        {
            Color = color;
        }

        public string? Color { get; }

        public override ClientApplicationDescription Description => new ClientApplicationDescription( Name, Name == "App" ? "/" : $"/{Name.ToLowerInvariant()}" );
    }

    public sealed class ServedClientFactory : IPartyClientFactory
    {
        public ServedClientFactory( ServingFeatureDriver serving ) => RequiredFeatures = [serving];

        public IReadOnlyCollection<string> ClientNames => ["App", "Admin"];

        public IReadOnlyList<ApplicationIdentityFeatureDriver> RequiredFeatures { get; }

        public PartyClient? Create( IActivityMonitor monitor, ILocalParty party, string name, ImmutableConfigurationSection configuration )
        {
            foreach( var c in configuration.GetChildren() )
            {
                if( c.Key != "Color" )
                {
                    monitor.Error( $"Unknown fact '{c.Key}' for the client '{name}' of '{party.FullName}'." );
                    return null;
                }
            }
            return new ServedClient( party, name, configuration, configuration["Color"] );
        }
    }

    /// <summary>
    /// A local client: not announced, no requirement.
    /// </summary>
    public sealed class LocalClient : PartyClient
    {
        public LocalClient( ILocalParty party, string name, ImmutableConfigurationSection configuration ) : base( party, name, configuration ) { }
    }

    public sealed class LocalClientFactory : IPartyClientFactory
    {
        public IReadOnlyCollection<string> ClientNames => ["Tool"];

        public IReadOnlyList<ApplicationIdentityFeatureDriver> RequiredFeatures => [];

        public PartyClient? Create( IActivityMonitor monitor, ILocalParty party, string name, ImmutableConfigurationSection configuration )
            => new LocalClient( party, name, configuration );
    }

    static IHost BuildHost( Action<MutableConfigurationSection> configure, params Type[] factories )
    {
        var ckApp = new MutableConfigurationSection( "CK-AppIdentity" );
        ckApp["FullName"] = "Clients/$App";
        configure( ckApp );
        var configuration = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, ckApp ).ShouldNotBeNull();
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Services.AddSingleton( configuration );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        // The order of the drivers is their dependencies' one (the engine does it in a real application).
        // The description driver comes first: it describes the parties once all the drivers have set them up.
        builder.Services.AddSingleton<PartyDescriptionFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<PartyDescriptionFeatureDriver>() );
        builder.Services.AddSingleton<ServingFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<ServingFeatureDriver>() );
        foreach( var f in factories )
        {
            builder.Services.AddSingleton( f );
            builder.Services.AddSingleton( sp => (IPartyClientFactory)sp.GetRequiredService( f ) );
        }
        builder.Services.AddSingleton<PartyClientFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<PartyClientFeatureDriver>() );
        return builder.CKBuild();
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_declared_clients_are_created_and_described_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c =>
        {
            c["Local:AllowFeatures"] = "Serving";
            c["Local:PublicUrl"] = "https://acme.com";
            c["Local:PartyClient:App:Color"] = "Blue";
            c["Local:PartyClient:Tool"] = "";
        }, typeof( ServedClientFactory ), typeof( LocalClientFactory ) );
        {
            await host.StartAsync( cancel );
            (await logs.WaitForAsync( "The client 'Admin' is built but not declared by 'Clients/$App/#Dev': it is not a client of this party. Add \"Local:PartyClient:Admin\" to its configuration to make it one." )).ShouldBeTrue();
        }
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        var clients = root.GetRequiredFeature<PartyClientFeature>().Clients;
        clients.Select( c => c.Name ).ShouldBe( ["App", "Tool"] );
        clients[0].ShouldBeOfType<ServedClient>().Color.ShouldBe( "Blue" );
        clients[1].ShouldBeOfType<LocalClient>();

        // The served client is announced, the local one is not.
        root.Description.ClientApplications.ShouldBe( [new ClientApplicationDescription( "App", "/" )] );
        root.Description.GetClientApplicationUrl().ShouldBe( "https://acme.com" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_declared_client_that_no_package_builds_is_an_error_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => c["Local:PartyClient:Unknown"] = "", typeof( LocalClientFactory ) );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "The client 'Unknown' is declared by 'Clients/$App/#Dev' but no package builds it." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_client_requires_the_features_of_its_package_Async( CancellationToken cancel )
    {
        // "Serving" is opt-in, not allowed here.
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => c["Local:PartyClient:App"] = "", typeof( ServedClientFactory ) );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "The client 'App' of 'Clients/$App/#Dev' requires the feature(s) 'Serving' that 'Clients/$App/#Dev' doesn't allow." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_fact_that_the_kind_of_client_doesnt_have_is_an_error_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c =>
        {
            c["Local:AllowFeatures"] = "Serving";
            c["Local:PartyClient:App:Origin"] = "https://cdn.acme.com";
        }, typeof( ServedClientFactory ) );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "Unknown fact 'Origin' for the client 'App' of 'Clients/$App/#Dev'." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_party_that_disallows_the_feature_has_no_clients_and_cannot_declare_any_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using( var host = BuildHost( c => c["Local:DisallowFeatures"] = "PartyClient", typeof( LocalClientFactory ) ) )
        {
            await host.StartAsync( cancel );
            host.Services.GetRequiredService<ApplicationIdentityService>().GetFeature<PartyClientFeature>().ShouldBeNull();
            await host.StopAsync( cancel );
        }
        using( var host = BuildHost( c =>
        {
            c["Local:DisallowFeatures"] = "PartyClient";
            c["Local:PartyClient:Tool"] = "";
        }, typeof( LocalClientFactory ) ) )
        {
            {
                await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
                (await logs.WaitForAsync( "'Clients/$App/#Dev' declares clients ('Tool') but doesn't allow the 'PartyClient' feature." )).ShouldBeTrue();
            }
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task two_packages_cannot_build_the_same_client_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => { }, typeof( LocalClientFactory ), typeof( OtherToolFactory ) );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( l => l.Contains( "The client 'Tool' is built by" ) && l.Contains( "a client name is built by one package only." ) )).ShouldBeTrue();
        }
    }

    public sealed class OtherToolFactory : IPartyClientFactory
    {
        public IReadOnlyCollection<string> ClientNames => ["Tool"];

        public IReadOnlyList<ApplicationIdentityFeatureDriver> RequiredFeatures => [];

        public PartyClient? Create( IActivityMonitor monitor, ILocalParty party, string name, ImmutableConfigurationSection configuration )
            => new LocalClient( party, name, configuration );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_dynamic_tenant_domain_has_its_clients_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => { }, typeof( LocalClientFactory ) );
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        root.GetRequiredFeature<PartyClientFeature>().Clients.ShouldBeEmpty();

        var domain = await root.AddTenantDomainAsync( TestHelper.Monitor, s =>
        {
            s["FullName"] = "D1/$D1";
            s["Local:PartyClient:Tool"] = "";
        } );
        domain.ShouldNotBeNull();
        domain.GetRequiredFeature<PartyClientFeature>().Clients.Single().ShouldBeOfType<LocalClient>().Party.ShouldBe( domain );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_clients_of_a_tenant_domain_are_in_its_description_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Local:AllowFeatures"] = "Serving", typeof( ServedClientFactory ) );
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        root.Description.ClientApplications.ShouldBeEmpty();

        var domain = await root.AddTenantDomainAsync( TestHelper.Monitor, s =>
        {
            s["FullName"] = "D1/$D1";
            s["Local:AllowFeatures"] = "Serving";
            s["Local:PublicUrl"] = "https://d1.acme.com";
            s["Local:PartyClient:Admin"] = "";
        } );
        domain.ShouldNotBeNull();
        domain.Description.ClientApplications.ShouldBe( [new ClientApplicationDescription( "Admin", "/admin" )] );
        domain.Description.GetClientApplicationUrl( "Admin" ).ShouldBe( "https://d1.acme.com/admin" );
        await host.StopAsync( cancel );
    }
}
