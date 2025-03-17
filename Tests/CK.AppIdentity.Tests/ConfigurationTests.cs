using CK.Core;
using Shouldly;
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

        config.FullName.ShouldBe( "LaToulousaine/France/Albi/$SignatureBox/#Dev" );
        config.DomainName.ShouldBe( "LaToulousaine/France/Albi" );
        config.PartyName.ShouldBe( "$SignatureBox" );
        config.EnvironmentName.ShouldBe( "#Dev" );

        config.Remotes.Count.ShouldBe( 3 );

        var logTower = config.Remotes.OfType<RemotePartyConfiguration>().Single( r => r.FullName == "Signature/SaaSCentral/$LogTower/#Prod" );
        logTower.DomainName.ShouldBe( "Signature/SaaSCentral" );
        logTower.EnvironmentName.ShouldBe( "#Prod" );
        logTower.PartyName.ShouldBe( "$LogTower" );
        logTower.Address.ShouldBe( "148.54.11.18:3712" );

        var trolleyCentral = config.Remotes.OfType<RemotePartyConfiguration>().Single( r => r.FullName == "LaToulousaine/London/$TrolleyCentral/#Dev" );
        trolleyCentral.DomainName.ShouldBe( "LaToulousaine/London" );
        trolleyCentral.EnvironmentName.ShouldBe( "#Dev" );
        trolleyCentral.PartyName.ShouldBe( "$TrolleyCentral" );
        trolleyCentral.Address.ShouldBeNull();

        var trolley1 = config.Remotes.OfType<RemotePartyConfiguration>().Single( r => r.FullName == "LaToulousaine/France/Albi/$Trolley1/#Dev" );
        trolley1.DomainName.ShouldBe( "LaToulousaine/France/Albi" );
        trolley1.EnvironmentName.ShouldBe( "#Dev" );
        trolley1.PartyName.ShouldBe( "$Trolley1" );
        trolley1.Address.ShouldBeNull();
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
        good.Remotes.ShouldBeEmpty();
        good.TenantDomains.Count.ShouldBe( 3 );
        good.TenantDomains.Select( d => d.FullName.Path ).ShouldBe( ["D1/$D1/#E", "D2/$D2/#E", "D3/$D3/#E"], ignoreOrder: true );
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
        identityConfig.DomainName.ShouldBe( "Default" );
        identityConfig.EnvironmentName.ShouldBe( "#HostEnv" );
        identityConfig.PartyName.ShouldBe( "$HostApp" );
        identityConfig.Remotes.ShouldBeEmpty();
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

        identityConfig.DomainName.ShouldBe( "OurDomain" );
        identityConfig.EnvironmentName.ShouldBe( "#TestEnvironment" );
        identityConfig.PartyName.ShouldBe( "$MyApp" );
        identityConfig.Remotes.Count.ShouldBe( 1 );
        var remote = identityConfig.Remotes.Single();
        Throw.DebugAssert( remote != null );
        remote.PartyName.ShouldBe( "$Daddy" );
        remote.Address.ShouldBe( "http://x.x" );
        remote.DomainName.ShouldBe( "OurDomain" );
        remote.EnvironmentName.ShouldBe( "#TestEnvironment" );
    }

}
