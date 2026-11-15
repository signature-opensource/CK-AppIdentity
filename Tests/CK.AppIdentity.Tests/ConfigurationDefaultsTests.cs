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
        ShouldFail( c => c["KeyManagement"] = "true",
                    "'CK-AppIdentity:KeyManagement' is not allowed: 'CK-AppIdentity' is the root" );
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
            c["Parties:0:Local:A"] = "true";
        }, "'CK-AppIdentity:Parties:0:Local' is not allowed: 'CK-AppIdentity:Parties:0' is a remote party" );
        ShouldFail( c =>
        {
            c["Parties:0:PartyName"] = "Remote";
            c["Parties:0:Defaults:A"] = "true";
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
            c["Defaults:A"] = "true";
            c["Defaults:Z"] = "false";
            c["Local:L:Setting"] = "1";
            c["Parties:0:PartyName"] = "R1";
            c["Parties:1:FullName"] = "Acme/Tenant/$Tenant";
            c["Parties:1:Defaults:Z"] = "true";
            c["Parties:1:Defaults:A"] = "false";
            c["Parties:1:Local:A"] = "true";
            c["Parties:1:Parties:0:PartyName"] = "R2";
            c["Parties:2:DomainName"] = "Acme/Group";
            c["Parties:2:Defaults:G"] = "true";
            c["Parties:2:Parties:0:PartyName"] = "R3";
            c["Parties:2:Parties:0:A"] = "false";
        } ).ShouldNotBeNull();

        // The root is a structural level: it declares nothing, its local party is its "Local".
        config.GetFeatureDeclaration( "A" ).ShouldBeNull();
        // A section declares its feature.
        config.LocalConfiguration.GetFeatureDeclaration( "L" ).ShouldBe( true );
        config.LocalConfiguration.GetFeatureDeclaration( "a" ).ShouldBe( true );
        config.LocalConfiguration.GetFeatureDeclaration( "Z" ).ShouldBe( false );
        config.LocalConfiguration.GetFeatureDeclaration( "G" ).ShouldBeNull();
        config.LocalConfiguration.GetBooleanKeys().ShouldBe( ["A", "Z"], ignoreOrder: true );

        var r1 = config.Remotes.Single( r => r.PartyName == "$R1" );
        r1.GetFeatureDeclaration( "A" ).ShouldBe( true );
        r1.GetFeatureDeclaration( "Z" ).ShouldBe( false );
        r1.GetFeatureDeclaration( "L" ).ShouldBeNull();

        // The nearest wins: the tenant's "Defaults", then its own keys.
        var tenant = config.TenantDomains.Single();
        tenant.GetFeatureDeclaration( "A" ).ShouldBeNull();
        tenant.LocalConfiguration.GetFeatureDeclaration( "A" ).ShouldBe( true );
        tenant.LocalConfiguration.GetFeatureDeclaration( "Z" ).ShouldBe( true );
        var r2 = tenant.Remotes.Single();
        r2.GetFeatureDeclaration( "A" ).ShouldBe( false );
        r2.GetFeatureDeclaration( "Z" ).ShouldBe( true );

        var r3 = config.Remotes.Single( r => r.PartyName == "$R3" );
        r3.GetFeatureDeclaration( "G" ).ShouldBe( true );
        r3.GetFeatureDeclaration( "A" ).ShouldBe( false );
        r3.GetFeatureDeclaration( "Z" ).ShouldBe( false );
        r3.IsAllowedFeature( "Other", isAllowedByDefault: true ).ShouldBeTrue();
        r3.IsAllowedFeature( "Other", isAllowedByDefault: false ).ShouldBeFalse();
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
            c["Defaults:Asym"] = "true";
            c["Parties:0:PartyName"] = "R1";
            c["Parties:1:PartyName"] = "R2";
            c["Parties:1:Asym"] = "false";
        } ).ShouldNotBeNull();
        var s = new ApplicationIdentityService( c, new ServiceCollection().BuildServiceProvider() );
        var driver = new FeatureBuilderInitializationTests.AsymFeatureDriver( s, isAllowedByDefault: false, featureName: null );
        driver.IsRootAllowed.ShouldBeTrue();
        driver.IsAllowedFeature( s ).ShouldBeTrue();
        driver.IsAllowedFeature( s.Remotes.Single( r => r.PartyName == "$R1" ) ).ShouldBeTrue();
        driver.IsAllowedFeature( s.Remotes.Single( r => r.PartyName == "$R2" ) ).ShouldBeFalse();

        // Declared only in the root's Local: the remotes don't have it.
        var c2 = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Acme/$App";
            c["Local:Asym"] = "true";
            c["Parties:0:PartyName"] = "R1";
        } ).ShouldNotBeNull();
        var s2 = new ApplicationIdentityService( c2, new ServiceCollection().BuildServiceProvider() );
        var driver2 = new FeatureBuilderInitializationTests.AsymFeatureDriver( s2, isAllowedByDefault: false, featureName: null );
        driver2.IsAllowedFeature( s2 ).ShouldBeTrue();
        driver2.IsAllowedFeature( s2.Remotes.Single() ).ShouldBeFalse();
    }
}
