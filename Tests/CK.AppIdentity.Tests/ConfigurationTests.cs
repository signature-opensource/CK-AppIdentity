using CK.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using NUnit.Framework;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;


[TestFixture]
public class ConfigurationTests
{
    [Test]
    public void basic_agent_configuration()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["DomainName"] = "LaToulousaine/France/Albi";
            c["PartyName"] = "SignatureBox";

            c["Parties:0:DomainName"] = "Signature/SaaSCentral";
            c["Parties:0:EnvironmentName"] = "#Prod";
            c["Parties:0:PartyName"] = "LogTower";
            c["Parties:0:Address"] = "148.54.11.18:3712";

            // The French $SignatureBox talks to the English $TrolleyCentral.
            c["Parties:1:FullName"] = "LaToulousaine/London/$TrolleyCentral";
            c["Parties:2:PartyName"] = "Trolley1";
        } );
        Throw.DebugAssert( config != null );

        config.FullName.Should().Be( "LaToulousaine/France/Albi/$SignatureBox/#Dev" );
        config.DomainName.Should().Be( "LaToulousaine/France/Albi" );
        config.PartyName.Should().Be( "$SignatureBox" );
        config.EnvironmentName.Should().Be( "#Dev" );

        config.Remotes.Should().HaveCount( 3 );

        var logTower = config.Remotes.OfType<RemotePartyConfiguration>().Single( r => r.FullName == "Signature/SaaSCentral/$LogTower/#Prod" );
        logTower.DomainName.Should().Be( "Signature/SaaSCentral" );
        logTower.EnvironmentName.Should().Be( "#Prod" );
        logTower.As<RemotePartyConfiguration>().PartyName.Should().Be( "$LogTower" );
        logTower.As<RemotePartyConfiguration>().Address.Should().Be( "148.54.11.18:3712" );

        var trolleyCentral = config.Remotes.OfType<RemotePartyConfiguration>().Single( r => r.FullName == "LaToulousaine/London/$TrolleyCentral/#Dev" );
        trolleyCentral.DomainName.Should().Be( "LaToulousaine/London" );
        trolleyCentral.EnvironmentName.Should().Be( "#Dev" );
        trolleyCentral.As<RemotePartyConfiguration>().PartyName.Should().Be( "$TrolleyCentral" );
        trolleyCentral.As<RemotePartyConfiguration>().Address.Should().BeNull();

        var trolley1 = config.Remotes.OfType<RemotePartyConfiguration>().Single( r => r.FullName == "LaToulousaine/France/Albi/$Trolley1/#Dev" );
        trolley1.DomainName.Should().Be( "LaToulousaine/France/Albi" );
        trolley1.EnvironmentName.Should().Be( "#Dev" );
        trolley1.As<RemotePartyConfiguration>().PartyName.Should().Be( "$Trolley1" );
        trolley1.As<RemotePartyConfiguration>().Address.Should().BeNull();
    }

    [Test]
    public void tenant_domains_can_define_tenant_domains_but_they_are_lifted()
    {
        var good = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "SaaSProduct/$SaaS1/#E";
            c["Parties:0:FullName"] = "D1/$D1";
            c["Parties:0:Parties:0:PartyName"] = "A1";
            c["Parties:0:Parties:1:FullName"] = "D2/$D2";
            c["Parties:0:Parties:1:Parties:0:PartyName"] = "A2";
            c["Parties:0:Parties:2:FullName"] = "D3/$D3";
        } );
        Throw.DebugAssert( good != null );
        good.Remotes.Should().BeEmpty();
        good.TenantDomains.Should().HaveCount( 3 );
        good.TenantDomains.Select( d => d.FullName.Path ).Should().BeEquivalentTo( ["D1/$D1/#E", "D2/$D2/#E", "D3/$D3/#E"] );
    }

    [Test]
    public void AppIdentityConfiguration_from_IHostEnvironment_and_IConfiguration_can_be_default()
    {
        var hostEnv = new HostingEnvironment()
        {
            ApplicationName = "HostApp",
            EnvironmentName = "HostEnv",
        };
        var config = new MutableConfigurationSection( "EmptyConfig" );
        var identityConfig = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, hostEnv, config );
        Throw.DebugAssert( identityConfig != null );
        identityConfig.DomainName.Should().Be( "Default" );
        identityConfig.EnvironmentName.Should().Be( "#HostEnv" );
        identityConfig.PartyName.Should().Be( "$HostApp" );
        identityConfig.Remotes.Should().BeEmpty();
    }

    [Test]
    public void AppIdentityConfiguration_from_IHostEnvironment_and_IConfiguration()
    {
        var hostEnv = new HostingEnvironment()
        {
            ApplicationName = "HostApp",
            EnvironmentName = "HostEnv",
        };
        var config = new MutableConfigurationSection( "FakePath" );
        config["CK-AppIdentity:DomainName"] = "OurDomain";
        config["CK-AppIdentity:EnvironmentName"] = "#TestEnvironment";
        config["CK-AppIdentity:PartyName"] = "MyApp";
        config["CK-AppIdentity:Parties:0:PartyName"] = "Daddy";
        config["CK-AppIdentity:Parties:0:Address"] = "http://x.x";
        var identityConfig = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, hostEnv, config.GetRequiredSection( "CK-AppIdentity" ) );
        Throw.DebugAssert( identityConfig != null );

        identityConfig.DomainName.Should().Be( "OurDomain" );
        identityConfig.EnvironmentName.Should().Be( "#TestEnvironment" );
        identityConfig.PartyName.Should().Be( "$MyApp" );
        identityConfig.Remotes.Should().HaveCount( 1 );
        var remote = identityConfig.Remotes.Single();
        Throw.DebugAssert( remote != null );
        remote.PartyName.Should().Be( "$Daddy" );
        remote.Address.Should().Be( "http://x.x" );
        remote.DomainName.Should().Be( "OurDomain" );
        remote.EnvironmentName.Should().Be( "#TestEnvironment" );
    }

}
