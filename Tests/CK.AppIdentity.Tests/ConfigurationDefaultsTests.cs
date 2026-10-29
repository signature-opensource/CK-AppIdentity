using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class ConfigurationDefaultsTests
{
    static void ShouldFail( Action<MutableConfigurationSection> configure, string expectedError )
    {
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "Acme/$App";
                configure( c );
            } ).ShouldBeNull();
            logs.ShouldContain( l => l.Contains( expectedError ), customMessage: expectedError );
        }
    }

    [Test]
    public void the_structural_levels_only_have_their_identity_and_their_structure()
    {
        ShouldFail( c => c["AllowFeatures"] = "A",
                    "'CK-AppIdentity:AllowFeatures' is not allowed: 'CK-AppIdentity' is the root" );
        ShouldFail( c => c["Assemblies:CK.Core"] = "C",
                    "'CK-AppIdentity:Assemblies' is not allowed: 'CK-AppIdentity' is the root" );
        ShouldFail( c =>
        {
            c["Parties:0:FullName"] = "Acme/Tenant/$Tenant";
            c["Parties:0:MaxClockOffset"] = "00:00:10";
        }, "'CK-AppIdentity:Parties:0:MaxClockOffset' is not allowed: 'CK-AppIdentity:Parties:0' is a tenant domain" );
        ShouldFail( c =>
        {
            c["Parties:0:DomainName"] = "Group";
            c["Parties:0:Options:X"] = "1";
            c["Parties:0:Parties:0:PartyName"] = "A";
        }, "'CK-AppIdentity:Parties:0:Options' is not allowed: 'CK-AppIdentity:Parties:0' is a group of parties" );
    }

    [Test]
    public void a_remote_has_no_Local_and_no_Defaults()
    {
        ShouldFail( c =>
        {
            c["Parties:0:PartyName"] = "Remote";
            c["Parties:0:Local:AllowFeatures"] = "A";
        }, "'CK-AppIdentity:Parties:0:Local' is not allowed: 'CK-AppIdentity:Parties:0' is a remote party" );
        ShouldFail( c =>
        {
            c["Parties:0:PartyName"] = "Remote";
            c["Parties:0:Defaults:AllowFeatures"] = "A";
        }, "'CK-AppIdentity:Parties:0:Defaults' is not allowed: 'CK-AppIdentity:Parties:0' is a remote party" );
    }

    [Test]
    public void Defaults_hold_values_not_an_identity_or_a_structure()
    {
        ShouldFail( c => c["Defaults:EnvironmentName"] = "#Prod",
                    "'CK-AppIdentity:Defaults:EnvironmentName' is not allowed: \"Defaults\" holds the default values of the parties" );
        ShouldFail( c => c["Defaults:Parties:0:PartyName"] = "A",
                    "'CK-AppIdentity:Defaults:Parties' is not allowed: \"Defaults\" holds the default values of the parties" );
    }

    [Test]
    public void the_features_are_resolved_along_the_Defaults_the_nearest_wins()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Defaults:AllowFeatures"] = "A";
            c["Defaults:DisallowFeatures"] = "Z";
            c["Local:AllowFeatures"] = "L";
            c["Parties:0:PartyName"] = "R1";
            c["Parties:1:FullName"] = "Acme/Tenant/$Tenant";
            c["Parties:1:Defaults:AllowFeatures"] = "Z";
            c["Parties:1:Defaults:DisallowFeatures"] = "A";
            c["Parties:1:Local:AllowFeatures"] = "A";
            c["Parties:1:Parties:0:PartyName"] = "R2";
            c["Parties:2:DomainName"] = "Acme/Group";
            c["Parties:2:Defaults:AllowFeatures"] = "G";
            c["Parties:2:Parties:0:PartyName"] = "R3";
            c["Parties:2:Parties:0:DisallowFeatures"] = "A";
        } ).ShouldNotBeNull();

        // The root is a structural level: its local party is its "Local".
        config.AllowFeatures.ShouldBeEmpty();
        config.DisallowFeatures.ShouldBeEmpty();
        config.LocalConfiguration.AllowFeatures.ShouldBe( ["A", "L"], ignoreOrder: true );
        config.LocalConfiguration.DisallowFeatures.ShouldBe( ["Z"] );

        var r1 = config.Remotes.Single( r => r.PartyName == "$R1" );
        r1.AllowFeatures.ShouldBe( ["A"] );
        r1.DisallowFeatures.ShouldBe( ["Z"] );

        var tenant = config.TenantDomains.Single();
        tenant.AllowFeatures.ShouldBeEmpty();
        tenant.LocalConfiguration.AllowFeatures.ShouldBe( ["Z", "A"], ignoreOrder: true );
        tenant.LocalConfiguration.DisallowFeatures.ShouldBeEmpty();
        var r2 = tenant.Remotes.Single();
        r2.AllowFeatures.ShouldBe( ["Z"] );
        r2.DisallowFeatures.ShouldBe( ["A"] );

        var r3 = config.Remotes.Single( r => r.PartyName == "$R3" );
        r3.AllowFeatures.ShouldBe( ["G"] );
        r3.DisallowFeatures.ShouldBe( ["Z", "A"], ignoreOrder: true );
    }

    [Test]
    public void the_assemblies_are_resolved_along_the_Defaults()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Defaults:Assemblies:CK.Core"] = "Core";
            c["Local:Assemblies:CK.AppIdentity"] = "App";
            c["Parties:0:PartyName"] = "R1";
        } ).ShouldNotBeNull();
        config.LocalConfiguration.AssemblyConfiguration.Assemblies.Values.ShouldBe( ["CK.Core", "CK.AppIdentity"], ignoreOrder: true );
        config.Remotes.Single().AssemblyConfiguration.Assemblies.Values.ShouldBe( ["CK.Core"] );
        config.AssemblyConfiguration.Assemblies.ShouldBeEmpty();
    }

    [Test]
    public void a_feature_driver_reads_a_local_party_in_its_Local()
    {
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Defaults:AllowFeatures"] = "Singleton";
            c["Parties:0:PartyName"] = "R1";
            c["Parties:1:PartyName"] = "R2";
            c["Parties:1:DisallowFeatures"] = "Singleton";
        } ).ShouldNotBeNull();
        var s = new ApplicationIdentityService( c, new ServiceCollection().BuildServiceProvider() );
        var driver = new FeatureBuilderInitializationTests.SingletonFeatureDriver( s, isAllowedByDefault: false );
        driver.IsRootAllowed.ShouldBeTrue();
        driver.IsAllowedFeature( s ).ShouldBeTrue();
        driver.IsAllowedFeature( s.Remotes.Single( r => r.PartyName == "$R1" ) ).ShouldBeTrue();
        driver.IsAllowedFeature( s.Remotes.Single( r => r.PartyName == "$R2" ) ).ShouldBeFalse();

        // Allowed only in the root's Local: the remotes don't have it.
        var c2 = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Local:AllowFeatures"] = "Singleton";
            c["Parties:0:PartyName"] = "R1";
        } ).ShouldNotBeNull();
        var s2 = new ApplicationIdentityService( c2, new ServiceCollection().BuildServiceProvider() );
        var driver2 = new FeatureBuilderInitializationTests.SingletonFeatureDriver( s2, isAllowedByDefault: false );
        driver2.IsAllowedFeature( s2 ).ShouldBeTrue();
        driver2.IsAllowedFeature( s2.Remotes.Single() ).ShouldBeFalse();
    }
}
