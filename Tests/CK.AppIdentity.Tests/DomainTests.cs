using CK.Core;
using CK.Testing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests
{
    [TestFixture]
    public class DomainTests
    {
        [Test]
        public async Task with_tenant_domains_initialization_Async()
        {
            var config = new DynamicConfigurationSource();
            using( config.StartBatch() )
            {
                config["CK-AppIdentity:DomainName"] = "SaaSProduct";
                config["CK-AppIdentity:PartyName"] = "SaaS1";
                config["CK-AppIdentity:EnvironmentName"] = "#Dev";
                config["CK-AppIdentity:Parties:0:FullName"] = "AllInOneInc/$AllInOneInc";
                config["CK-AppIdentity:Parties:0:Parties:0:PartyName"] = "ControlBox";
                config["CK-AppIdentity:Parties:0:Parties:1:PartyName"] = "Hall1Wall";
                config["CK-AppIdentity:Parties:0:Parties:2:PartyName"] = "Hall2Wall";
                config["CK-AppIdentity:Parties:0:Parties:3:PartyName"] = "Hall1Trolley";
                config["CK-AppIdentity:Parties:0:Parties:4:PartyName"] = "Hall2Trolley";
                config["CK-AppIdentity:Parties:1:FullName"] = "OpalCorp/$OpalCorp";
                config["CK-AppIdentity:Parties:1:Parties:0:PartyName"] = "ControlBox";
                config["CK-AppIdentity:Parties:1:Parties:1:PartyName"] = "MeasureStation";
            }
            var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
            builder.Configuration.Sources.Add( config );
            builder.Services.AddSingleton<ApplicationIdentityService>();
            builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );

            using var app = builder.AddApplicationIdentityServiceConfiguration()
                                   .CKBuild();

            await app.StartAsync();

            var appIdentityService = app.Services.GetRequiredService<ApplicationIdentityService>();

            appIdentityService.DomainName.Should().Be( "SaaSProduct" );
            appIdentityService.EnvironmentName.Should().Be( "#Dev" );
            appIdentityService.PartyName.Should().Be( "$SaaS1" );
            appIdentityService.Parties.Should().HaveCount( 2 );

            var allInOne = appIdentityService.TenantDomains.Single( d => d.PartyName == "$AllInOneInc" );
            allInOne.Configuration.EnvironmentName.Should().Be( "#Dev" );
            allInOne.Remotes.Should().HaveCount( 5, "There are 5 agents in this group." );
            allInOne.Remotes.Should().AllSatisfy( r =>
            {
                new[] { "$ControlBox", "$Hall1Wall", "$Hall2Wall", "$Hall1Trolley", "$Hall2Trolley" }.Should().Contain( r.PartyName );
                r.DomainName.Should().Be( "AllInOneInc" );
                r.EnvironmentName.Should().Be( "#Dev" );
            } );
            var opal = appIdentityService.TenantDomains.Single( p => p.PartyName == "$OpalCorp" );
            opal.Remotes.Should().HaveCount( 2, "There are 2 agents in this domain." );
            opal.Remotes.Should().AllSatisfy( r =>
            {
                new[] { "$ControlBox", "$MeasureStation" }.Should().Contain( r.PartyName );
                r.DomainName.Should().Be( "OpalCorp" );
                r.EnvironmentName.Should().Be( "#Dev" );
            } );
        }

        [Test]
        public async Task homonyms_are_disallowed_Parties_must_be_destroyed_before_being_added_Async()
        {
            var config = new MutableConfigurationSection( "DontCare" );
            config["CK-AppIdentity:FullName"] = "D/$P";

            var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
            builder.Configuration.Sources.Add( new ChainedConfigurationSource { Configuration = config } );
            builder.Services.AddSingleton<ApplicationIdentityService>();
            builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
            using var app = builder.AddApplicationIdentityServiceConfiguration()
                                   .CKBuild();

            await app.StartAsync();

            var s = app.Services.GetRequiredService<ApplicationIdentityService>();
            s.AllParties.Should().HaveCount( 0 );

            // This is the Agent "D/$P".
            IOwnedParty? noWay = await s.AddRemoteAsync( TestHelper.Monitor, s => s["FullName"] = "D/$P" );
            noWay.Should().BeNull();

            (await s.AddRemoteAsync( TestHelper.Monitor, s => s["PartyName"] = "$P1" )).Should().NotBeNull();

            noWay = await s.AddRemoteAsync( TestHelper.Monitor, s => s["FullName"] = "D/$P" );
            noWay.Should().BeNull( "Adding a Party that is the application is not possible." );

            // Adding a Tenant D1.
            var d1 = await s.AddTenantDomainAsync( TestHelper.Monitor, s => s["FullName"] = "D1/$D1" );
            Throw.DebugAssert( d1 != null );

            noWay = await s.AddTenantDomainAsync( TestHelper.Monitor, s => s["FullName"] = "D1/$D1" );
            noWay.Should().BeNull( "Duplicate tenant." );

            noWay = await d1.AddRemoteAsync( TestHelper.Monitor, s => s["FullName"] = "D/$P" );
            noWay.Should().BeNull();

            var p1 = await d1.AddRemoteAsync( TestHelper.Monitor, s => s["FullName"] = "D1/$P1" );
            noWay.Should().BeNull( "Adding a Party that is the application is not possible (in the tenant)." );

            noWay = await d1.AddRemoteAsync( TestHelper.Monitor, s => s["FullName"] = "D1/$P1" );
            noWay.Should().BeNull( "Duplicate remote (in the defining tenant) is obviously not possible." );
            noWay = await s.AddRemoteAsync( TestHelper.Monitor, s => s["FullName"] = "D1/$P1" );
            noWay.Should().BeNull( "Duplicate remote (in the application) is not possible." );

            // Adding a Tenant D2.
            var d2 = await s.AddTenantDomainAsync( TestHelper.Monitor, s => s["FullName"] = "D2/$D2" );
            Throw.DebugAssert( d2 != null );
            noWay = await d2.AddRemoteAsync( TestHelper.Monitor, s => s["FullName"] = "D1/$P1" );
            noWay.Should().BeNull( "Duplicate remote (from any other tenant) is not possible." );
        }

        [Test]
        public async Task with_empty_configuration_initialization_and_empty_services_Async()
        {
            var c = ApplicationIdentityServiceConfiguration.CreateEmpty();
            var empty = new ApplicationIdentityService( c, new SimpleServiceContainer() );
            // This does not throw.
            empty.InitializationTask.IsCompleted.Should().BeFalse();
            await ((IHostedService)empty).StartAsync( default );
            await empty.InitializationTask;
            await ((IHostedService)empty).StopAsync( default );
        }

    }
}
