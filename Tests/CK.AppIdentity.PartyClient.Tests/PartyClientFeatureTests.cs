using CK.Core;
using CK.Testing;
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
        public ServingFeatureDriver( ApplicationIdentityService s ) : base( s ) { }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context ) => Task.FromResult( true );

        protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

        protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

        protected override Task TeardownAsync( FeatureLifetimeContext context ) => Task.CompletedTask;
    }

    /// <summary>
    /// A served client feature ("Served"): it requires the opt-in "Serving" feature of its party, it gives a request path
    /// derived from its client name and has a "Color".
    /// </summary>
    [Requires<ServingFeatureDriver>]
    public sealed class ServedClientFeature : ClientFeature
    {
        public ServedClientFeature( PartyClient client, ImmutableConfigurationSection configuration )
            : base( client, configuration )
        {
            Color = configuration["Color"];
        }

        public string? Color { get; }

        protected override void ContributeDescription( ClientDescriptionBuilder builder )
        {
            builder.RequestPath = Client.Name == "App" ? "/" : $"/{Client.Name.ToLowerInvariant()}";
        }
    }

    public sealed class ServedClientFeatureDriver : ClientFeatureDriver<ServedClientFeature>
    {
        protected override ServedClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
        {
            foreach( var c in configuration.GetChildren() )
            {
                if( c.Key != "Color" )
                {
                    monitor.Error( $"Unknown key \"{c.Path}\" of {client}." );
                    return null;
                }
            }
            return new ServedClientFeature( client, configuration );
        }
    }

    /// <summary>
    /// A local client feature ("Tool"): not served, no requirement.
    /// </summary>
    public sealed class ToolClientFeature : ClientFeature
    {
        public ToolClientFeature( PartyClient client, ImmutableConfigurationSection configuration ) : base( client, configuration ) { }
    }

    public sealed class ToolClientFeatureDriver : ClientFeatureDriver<ToolClientFeature>
    {
        protected override ToolClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration ) => new( client, configuration );
    }

    static IHost BuildHost( Action<MutableConfigurationSection> configure, params Type[] drivers )
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
        // What the engine generates in a real application.
        builder.Services.AddSingleton( AppIdentityTestHelper.CreateComponentDirectory( typeof( PartyDescriptionFeatureDriver ),
                                                                                      typeof( ServingFeatureDriver ),
                                                                                      typeof( PartyClientFeatureDriver ),
                                                                                      typeof( ServedClientFeature ),
                                                                                      typeof( ToolClientFeature ) ) );
        foreach( var d in drivers )
        {
            builder.Services.AddSingleton( d );
            builder.Services.AddSingleton( sp => (IClientFeatureDriver)sp.GetRequiredService( d ) );
        }
        builder.Services.AddSingleton<PartyClientFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<PartyClientFeatureDriver>() );
        return builder.CKBuild();
    }

    static readonly Type[] _drivers = [typeof( ServedClientFeatureDriver ), typeof( ToolClientFeatureDriver )];

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_declared_clients_are_created_with_their_features_and_described_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c =>
        {
            c["Local:Serving"] = "true";
            c["Local:PublicUrl"] = "https://acme.com";
            c["Local:PartyClient:App:Served:Color"] = "Blue";
            // A feature without configuration is declared by true.
            c["Local:PartyClient:Tool:Tool"] = "true";
            // The type set of a client is compile time: accepted, ignored.
            c["Local:PartyClient:Tool:Types:0"] = "Some.Type, Some.Assembly";
        }, _drivers );
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        var clients = root.GetRequiredFeature<PartyClientFeature>().Clients;
        clients.Select( c => c.Name ).ShouldBe( ["App", "Tool"] );
        clients[0].GetFeature<ServedClientFeature>().ShouldNotBeNull().Color.ShouldBe( "Blue" );
        clients[0].GetFeature<ServedClientFeature>()!.Name.ShouldBe( "Served" );
        clients[1].Features.ShouldHaveSingleItem().ShouldBeOfType<ToolClientFeature>();

        // The served client is announced, the local one is not.
        root.Description.ClientApplications.ShouldBe( [new ClientApplicationDescription( "App", "/" )] );
        root.Description.GetClientApplicationUrl().ShouldBe( "https://acme.com" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_key_of_a_client_that_is_no_feature_is_an_error_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => c["Local:PartyClient:App:Unknown"] = "true", _drivers );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "Unknown \"CK-AppIdentity:Local:PartyClient:App:Unknown\": the keys of a client are its features (Served, Tool) and its type set (Types, ExcludedTypes)." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_feature_is_declared_by_true_or_its_section_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => c["Local:PartyClient:App:Tool"] = "maybe", _drivers );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "Invalid \"CK-AppIdentity:Local:PartyClient:App:Tool\" = 'maybe': a client feature is declared by true or by its section." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_client_without_feature_is_an_error_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => c["Local:PartyClient:App"] = "", _drivers );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "The client 'App' of 'Clients/$App/#Dev' has no feature." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_client_feature_requires_the_features_of_the_party_that_it_requires_Async( CancellationToken cancel )
    {
        // "Serving" is opt-in, not allowed here.
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c => c["Local:PartyClient:App:Served"] = "true", _drivers );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "'ServedClientFeature' of client 'App' of 'Clients/$App/#Dev' requires 'ServingFeatureDriver' that client 'App' of 'Clients/$App/#Dev' doesn't have." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_driver_validates_the_section_of_its_feature_Async( CancellationToken cancel )
    {
        using var logs = new AllMonitorsTexts();
        using var host = BuildHost( c =>
        {
            c["Local:Serving"] = "true";
            c["Local:PartyClient:App:Served:Origin"] = "https://cdn.acme.com";
        }, _drivers );
        {
            await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
            (await logs.WaitForAsync( "Unknown key \"CK-AppIdentity:Local:PartyClient:App:Served:Origin\" of client 'App' of 'Clients/$App/#Dev'." )).ShouldBeTrue();
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_party_that_turns_the_feature_off_has_no_clients_Async( CancellationToken cancel )
    {
        // Declaring a client declares the feature (its section): "PartyClient": false and a client can't coexist.
        using( var host = BuildHost( c => c["Local:PartyClient"] = "false", _drivers ) )
        {
            await host.StartAsync( cancel );
            host.Services.GetRequiredService<ApplicationIdentityService>().GetFeature<PartyClientFeature>().ShouldBeNull();
            await host.StopAsync( cancel );
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_dynamic_tenant_domain_has_its_clients_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => { }, _drivers );
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        root.GetRequiredFeature<PartyClientFeature>().Clients.ShouldBeEmpty();

        var domain = await root.AddTenantDomainAsync( TestHelper.Monitor, s =>
        {
            s["FullName"] = "D1/$D1";
            s["Local:PartyClient:Tool:Tool"] = "true";
        } );
        domain.ShouldNotBeNull();
        var tool = domain.GetRequiredFeature<PartyClientFeature>().Clients.Single();
        tool.Party.ShouldBe( domain );
        tool.Features.ShouldHaveSingleItem().ShouldBeOfType<ToolClientFeature>();
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_clients_of_a_tenant_domain_are_in_its_description_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Local:Serving"] = "true", _drivers );
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        root.Description.ClientApplications.ShouldBeEmpty();

        var domain = await root.AddTenantDomainAsync( TestHelper.Monitor, s =>
        {
            s["FullName"] = "D1/$D1";
            s["Local:Serving"] = "true";
            s["Local:PublicUrl"] = "https://d1.acme.com";
            s["Local:PartyClient:Admin:Served"] = "true";
        } );
        domain.ShouldNotBeNull();
        domain.Description.ClientApplications.ShouldBe( [new ClientApplicationDescription( "Admin", "/admin" )] );
        domain.Description.GetClientApplicationUrl( "Admin" ).ShouldBe( "https://d1.acme.com/admin" );
        await host.StopAsync( cancel );
    }
    [TestCase( "Enlistment", "", true )]
    [TestCase( "Enlistment", "app-identity/enlist", true )]
    [TestCase( "A|B", "", true )]
    [TestCase( "", "", false )]
    [TestCase( "A=B", "", false )]
    [TestCase( "A,B", "", false )]
    [TestCase( "A B", "", false )]
    [TestCase( "A", "x\ny", false )]
    public void a_fact_is_a_key_with_an_optional_value( string key, string value, bool isValid )
    {
        if( isValid )
        {
            var a = new PartyClientFactAttribute( key, value );
            a.Key.ShouldBe( key );
            a.Value.ShouldBe( value );
        }
        else
        {
            Should.Throw<ArgumentException>( () => new PartyClientFactAttribute( key, value ) );
        }
    }
}
