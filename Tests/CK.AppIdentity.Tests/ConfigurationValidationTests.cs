using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class ConfigurationValidationTests
{
    [TestCase( "External/$Stripe" )]
    [TestCase( "external/$Stripe" )]
    [TestCase( "Undefined/$Stripe" )]
    public void External_and_Undefined_domains_are_external_parties( string remoteFullName )
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Parties:0:FullName"] = remoteFullName;
        } );
        Throw.DebugAssert( config != null );
        var remote = config.Remotes.Single();
        remote.DomainName.ShouldBe( "External" );
        remote.IsExternalParty.ShouldBeTrue();
    }

    [TestCase( "ExternalPartners" )]
    [TestCase( "Externals/Eu" )]
    [TestCase( "UndefinedCorp" )]
    public void domains_that_only_start_with_External_or_Undefined_are_regular_domains( string domainName )
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Parties:0:DomainName"] = domainName;
            c["Parties:0:PartyName"] = "Remote";
        } );
        Throw.DebugAssert( config != null );
        var remote = config.Remotes.Single();
        remote.DomainName.ShouldBe( domainName );
        remote.IsExternalParty.ShouldBeFalse();
    }

    [TestCase( "External/Sub/$P" )]
    [TestCase( "Undefined/Sub/$P" )]
    public void External_sub_domains_are_errors( string remoteFullName )
    {
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Parties:0:FullName"] = remoteFullName;
        } ).ShouldBeNull();
    }

    [Test]
    public void the_root_domain_cannot_be_external_even_by_default()
    {
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Undefined/$App" ).ShouldBeNull();
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["DomainName"] = "External" ).ShouldBeNull();

        var section = new MutableConfigurationSection( "CK-AppIdentity" );
        section["PartyName"] = "App";
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, section, defaultDomainName: CoreApplicationIdentity.DefaultDomainName )
            .ShouldBeNull();
    }

    [TestCase( "Acme.Service" )]
    [TestCase( "" )]
    public void invalid_default_party_names_are_errors( string applicationName )
    {
        var hostEnv = new HostingEnvironment() { ApplicationName = applicationName, EnvironmentName = "Production" };
        var section = new MutableConfigurationSection( "CK-AppIdentity" );
        using( TestHelper.Monitor.CollectEntries( out var entries ) )
        {
            ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, hostEnv, section ).ShouldBeNull();
            entries.ShouldContain( e => e.Text.Contains( "PartyName" ) );
        }
        // Configuring the PartyName fixes it.
        section["PartyName"] = "AcmeService";
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, hostEnv, section );
        Throw.DebugAssert( config != null );
        config.FullName.Path.ShouldBe( "Default/$AcmeService/#Production" );
    }

    [Test]
    public void invalid_default_environment_names_are_errors()
    {
        var hostEnv = new HostingEnvironment() { ApplicationName = "App", EnvironmentName = "Staging.EU" };
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, hostEnv, new MutableConfigurationSection( "CK-AppIdentity" ) )
            .ShouldBeNull();
    }

    [TestCase( "development" )]
    [TestCase( "Development" )]
    public void Development_host_environment_is_Dev( string hostEnvironmentName )
    {
        var hostEnv = new HostingEnvironment() { ApplicationName = "App", EnvironmentName = hostEnvironmentName };
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, hostEnv, new MutableConfigurationSection( "CK-AppIdentity" ) );
        Throw.DebugAssert( config != null );
        config.EnvironmentName.ShouldBe( "#Dev" );
        config.StrictConfigurationMode.ShouldBeFalse();
    }

    [Test]
    public void configured_Development_environment_is_Dev()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["DomainName"] = "Acme";
            c["PartyName"] = "App";
            c["EnvironmentName"] = "#development";
            c["Parties:0:FullName"] = "Acme/$Other/#Development";
        } );
        Throw.DebugAssert( config != null );
        config.EnvironmentName.ShouldBe( "#Dev" );
        config.Remotes.Single().EnvironmentName.ShouldBe( "#Dev" );
    }

    [Test]
    public void invalid_names_report_their_own_syntax()
    {
        using( TestHelper.Monitor.CollectEntries( out var entries ) )
        {
            ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["DomainName"] = "Bad//Domain";
                c["PartyName"] = "App";
            } ).ShouldBeNull();
            var error = entries.Single( e => e.Text.Contains( "DomainName" ) );
            error.Text.ShouldContain( "path of identifiers" );
            error.Text.ShouldNotContain( "must start with a '#'" );
        }
    }

    [Test]
    public void only_the_Parties_of_a_group_are_parties()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Parties:0:DomainName"] = "Group";
            c["Parties:0:Options:PartyName"] = "NotAParty";
            c["Parties:0:Local:PartyName"] = "NotAPartyEither";
            c["Parties:0:Parties:0:PartyName"] = "A";
        } );
        Throw.DebugAssert( config != null );
        config.Remotes.Select( r => r.FullName.Path ).ShouldBe( ["Group/$A/#Dev"] );
    }

    [Test]
    public void ignored_sections_are_warnings_that_fail_in_strict_mode()
    {
        CreateStrict( c => c["Parties:0:DomainName"] = "EmptyGroup" ).ShouldBeNull();
        CreateStrict( c =>
        {
            c["Parties:0:PartyName"] = "Remote";
            c["Parties:0:Parties:0:PartyName"] = "Lost";
        } ).ShouldBeNull();
        CreateStrict( c => c["Parties:0:PartyName"] = "Remote" ).ShouldNotBeNull( "No warning: the configuration is valid." );

        static ApplicationIdentityServiceConfiguration? CreateStrict( Action<MutableConfigurationSection> configure )
        {
            return ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "Acme/$App";
                c["StrictConfigurationMode"] = "true";
                configure( c );
            } );
        }
    }

    [Test]
    public void dynamic_configurations_are_checked_with_the_strict_mode()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Acme/$App" );
        Throw.DebugAssert( config != null );
        static void RemoteWithLostParties( MutableConfigurationSection c )
        {
            c["PartyName"] = "Remote";
            c["Parties:0:PartyName"] = "Lost";
        }
        config.CreateDynamicRemoteConfiguration( TestHelper.Monitor, RemoteWithLostParties, strictConfigurationMode: true ).ShouldBeNull();
        config.CreateDynamicRemoteConfiguration( TestHelper.Monitor, RemoteWithLostParties ).ShouldNotBeNull( "Only a warning." );
    }

    [Test]
    public void warnings_are_counted_even_if_the_monitor_filters_them_out()
    {
        var before = TestHelper.Monitor.MinimalFilter;
        using( TestHelper.Monitor.TemporarilySetMinimalFilter( LogFilter.Release ) )
        {
            ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "Acme/$App";
                c["StrictConfigurationMode"] = "true";
                c["Parties:0:DomainName"] = "EmptyGroup";
            } ).ShouldBeNull();
            TestHelper.Monitor.MinimalFilter.ShouldBe( LogFilter.Release, "The filter is restored." );
        }
        TestHelper.Monitor.MinimalFilter.ShouldBe( before );
    }

    [TestCase( "App" )]
    [TestCase( "app" )]
    public void a_remote_cannot_have_the_FullName_of_the_local_party( string remotePartyName )
    {
        ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Parties:0:PartyName"] = remotePartyName;
        } ).ShouldBeNull();
    }

    [Test]
    public void tenant_detection_is_case_insensitive()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Parties:0:FullName"] = "Tenant/$tenant";
        } );
        Throw.DebugAssert( config != null );
        config.TenantDomains.Single().FullName.Path.ShouldBe( "Tenant/$tenant/#Dev" );
    }

    [TestCase( "a//b", "App", "#Dev" )]
    [TestCase( "Undefined", "App", "#Dev" )]
    [TestCase( "External/Sub", "App", "#Dev" )]
    [TestCase( "Acme", "", "#Dev" )]
    [TestCase( "Acme", "x.y", "#Dev" )]
    [TestCase( "Acme", "App", "Prod" )]
    public void CreateEmpty_validates_its_names( string domainName, string partyName, string environmentName )
    {
        Should.Throw<ArgumentException>( () => ApplicationIdentityServiceConfiguration.CreateEmpty( domainName, partyName, environmentName ) );
    }

    [Test]
    public void CreateEmpty_defaults()
    {
        var config = ApplicationIdentityServiceConfiguration.CreateEmpty();
        config.FullName.Path.ShouldBe( "Default/$Unknown/#Dev" );
        ApplicationIdentityServiceConfiguration.CreateEmpty( environmentName: "#Development" ).EnvironmentName.ShouldBe( "#Dev" );
    }

    [Test]
    public void DefaultStoreRootPath_is_settled()
    {
        var current = ApplicationIdentityServiceConfiguration.DefaultStoreRootPath;
        Should.NotThrow( () => ApplicationIdentityServiceConfiguration.DefaultStoreRootPath = current );
        Should.Throw<InvalidOperationException>( () => ApplicationIdentityServiceConfiguration.DefaultStoreRootPath = current.AppendPart( "Other" ) );
        Should.Throw<ArgumentException>( () => ApplicationIdentityServiceConfiguration.DefaultStoreRootPath = "Relative/Path" );
        ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.ShouldBe( current );
    }
}
