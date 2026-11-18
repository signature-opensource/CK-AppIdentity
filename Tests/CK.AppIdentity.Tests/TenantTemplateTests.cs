using CK.Core;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

// The tenant templates (UC step 4.3): an Opened one holds run-time tenant domains, named by their creator, whose index is
// kept in their record ("-Local/Instance.json").
[TestFixture]
public class TenantTemplateTests
{
    static readonly TimeSpan _timeout = TimeSpan.FromSeconds( 10 );

    // Counts the calls to ForgetLocalPartyAsync: a removed tenant is forgotten by the drivers.
    [PartyFeature( "Forgetful", TypeActivation.ByDefault )]
    public sealed class ForgetfulFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        public ForgetfulFeatureDriver( ApplicationIdentityService s ) : base( s ) { }

        public static readonly List<NormalizedPath> Forgotten = new();

        protected internal override Task ForgetLocalPartyAsync( IActivityMonitor monitor, NormalizedPath fullName, IFileStore localStore )
        {
            lock( Forgotten ) Forgotten.Add( fullName );
            // A private key: it must be deleted by its driver, never trashed.
            File.Delete( localStore.FolderPath.AppendPart( "Secret.key" ) );
            return Task.CompletedTask;
        }
    }

    static ApplicationIdentityServiceConfiguration? Configure( string domain, Action<MutableConfigurationSection>? configure = null, int maxInstanceCount = 3 )
    {
        return ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = $"{domain}/$Holder";
            c["Parties:0:DomainName"] = $"{domain}/Customers/{{Name}}";
            c["Parties:0:PartyName"] = "${Name}";
            c["Parties:0:Instances:0"] = "Configured";
            c["Parties:0:MaxInstanceCount"] = maxInstanceCount.ToString( System.Globalization.CultureInfo.InvariantCulture );
            configure?.Invoke( c );
        } );
    }

    static async Task<ApplicationIdentityService> StartAsync( string domain, Action<MutableConfigurationSection>? configure = null, int maxInstanceCount = 3 )
    {
        var c = Configure( domain, configure, maxInstanceCount ).ShouldNotBeNull();
        var services = new ServiceCollection();
        services.AddApplicationIdentityForTests( c, [typeof( ForgetfulFeatureDriver )] );
        var s = services.BuildServiceProvider().GetRequiredService<ApplicationIdentityService>();
        await s.StartAndInitializeAsync().WaitAsync( _timeout );
        return s;
    }

    // The domain folders of a test are cleared: the store is shared by the runs.
    static void Clear( string domain )
    {
        var f = ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.Combine( $"#Dev/{domain}" );
        if( Directory.Exists( f ) ) Directory.Delete( f, recursive: true );
    }

    static NormalizedPath LocalFolder( IApplicationIdentityService s, NormalizedPath fullName )
    {
        return s.TenantDomains.Single( t => t.FullName == fullName ).LocalFileStore.FolderPath;
    }

    [Test]
    public void an_Opened_tenant_template_is_kept_and_its_configured_instances_are_expanded()
    {
        var c = Configure( "TTRead" ).ShouldNotBeNull();
        var t = c.TenantTemplates.ShouldHaveSingleItem();
        t.DomainPrefix.ShouldBe( "TTRead/Customers" );
        t.Instances.IsOpened.ShouldBeTrue();
        t.Instances.TemplateName.ShouldBe( "Tenant" );
        t.GetFullName( "Acme" ).Path.ShouldBe( "TTRead/Customers/Acme/$Acme/#Dev" );
        var configured = c.TenantDomains.ShouldHaveSingleItem();
        configured.FullName.Path.ShouldBe( "TTRead/Customers/Configured/$Configured/#Dev" );
        configured.Template.ShouldBeSameAs( t );
        configured.IsDynamicInstance.ShouldBeFalse();
    }

    [Test]
    public void the_tenant_qualifier_resolves_in_an_inner_template()
    {
        var c = Configure( "TTQualifier", c =>
        {
            c["Parties:0:Parties:0:PartyName"] = "$Worker-{Name}";
            c["Parties:0:Parties:0:InstanceCount"] = "1";
            c["Parties:0:Parties:0:Address"] = "tcp:127.0.0.1:{5400+Tenant.Index}";
        } ).ShouldNotBeNull();
        var configured = c.TenantDomains.ShouldHaveSingleItem();
        configured.Remotes.ShouldHaveSingleItem().Address.ShouldBe( "tcp:127.0.0.1:5401" );
        var acme = c.TenantTemplates[0].CreateInstance( TestHelper.Monitor, c.TenantTemplates[0].Instances.CreateDynamic( 3, "Acme" ) ).ShouldNotBeNull();
        acme.Remotes.ShouldHaveSingleItem().Address.ShouldBe( "tcp:127.0.0.1:5403" );
    }

    [TestCase( "FullName", "must have a \"DomainName\" that ends with \"/{Name}\"" )]
    [TestCase( "PartyName", "must have a \"DomainName\" that ends with \"/{Name}\"" )]
    [TestCase( "Nested", "is a tenant template inside a tenant template" )]
    [TestCase( "IndexInName", "uses the index of the tenant" )]
    public void the_shape_of_a_tenant_template_is_checked( string error, string expected )
    {
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "TTShape/$Holder";
                c["Parties:0:MaxInstanceCount"] = "3";
                switch( error )
                {
                    case "FullName":
                        c["Parties:0:FullName"] = "TTShape/Customers/{Name}/${Name}";
                        break;
                    case "PartyName":
                        c["Parties:0:DomainName"] = "TTShape/Customers/T-{Name}";
                        c["Parties:0:PartyName"] = "$T-{Name}";
                        break;
                    default:
                        c["Parties:0:DomainName"] = "TTShape/Customers/{Name}";
                        c["Parties:0:PartyName"] = "${Name}";
                        if( error == "Nested" )
                        {
                            c["Parties:0:Parties:0:DomainName"] = "TTShape/Customers/{Tenant.Name}/{Name}";
                            c["Parties:0:Parties:0:PartyName"] = "${Name}";
                            c["Parties:0:Parties:0:InstanceCount"] = "1";
                        }
                        else
                        {
                            c["Parties:0:Parties:0:PartyName"] = "$Worker-{Tenant.Index}";
                        }
                        break;
                }
            } ).ShouldBeNull();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public async Task a_runtime_tenant_is_created_kept_moved_and_removed_Async()
    {
        Clear( "TTLife" );
        NormalizedPath acmeName;
        NormalizedPath acmeLocal;
        var s = await StartAsync( "TTLife" );
        await using( s )
        {
            var template = s.TenantTemplates.ShouldHaveSingleItem();
            var acme = (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "Acme" )).ShouldNotBeNull();
            acme.Configuration.IsDynamicInstance.ShouldBeTrue();
            acme.Configuration.Instance.ShouldNotBeNull().Index.ShouldBe( 2, "The first free run-time index (1 is configured)." );
            acme.IsDynamic.ShouldBeTrue();
            acmeName = acme.FullName;
            acmeLocal = acme.LocalFileStore.FolderPath;
            File.ReadAllText( acmeLocal.AppendPart( "Instance.json" ) ).ShouldContain( "\"Index\": 2" );
            // A private key of a driver.
            File.WriteAllText( acmeLocal.AppendPart( "Secret.key" ), "secret" );

            // The names are unique: a configured name, an existing tenant, an invalid name.
            (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "Configured" )).ShouldBeNull();
            (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "acme" )).ShouldBeNull();
            (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "3rd" )).ShouldBeNull();
            // The bound: 3 instances at most (1 configured, 2 run-time).
            (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "Beta" )).ShouldNotBeNull().Configuration.Instance!.Index.ShouldBe( 3 );
            (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "Gamma" )).ShouldBeNull();
            (await s.RemoveTenantInstanceAsync( TestHelper.Monitor, template.GetFullName( "Beta" ) )).ShouldBeTrue();
        }
        // Restarted: the run-time tenant is created again from its record.
        s = await StartAsync( "TTLife" );
        await using( s )
        {
            var acme = s.TenantDomains.Single( t => t.FullName == acmeName );
            acme.Configuration.Instance!.Index.ShouldBe( 2 );
            s.TenantDomains.Count.ShouldBe( 2, "The configured one and Acme: Beta has been removed." );

            // Moved: its index changes, not its identity.
            (await s.SetTenantIndexAsync( TestHelper.Monitor, acmeName, 3 )).ShouldBeTrue();
            var moved = s.TenantDomains.Single( t => t.FullName == acmeName );
            moved.ShouldNotBeSameAs( acme, "A moved tenant is created again." );
            moved.Configuration.Instance!.Index.ShouldBe( 3 );
            File.ReadAllText( acmeLocal.AppendPart( "Instance.json" ) ).ShouldContain( "\"Index\": 3" );
            (await s.SetTenantIndexAsync( TestHelper.Monitor, acmeName, 1 )).ShouldBeFalse( "1 is a configured index." );
            (await s.SetTenantIndexAsync( TestHelper.Monitor, s.TenantTemplates[0].GetFullName( "Configured" ), 2 )).ShouldBeFalse( "A configured tenant is not moved." );

            // Removed: forgotten by the drivers (its key is deleted), its private store trashed.
            ForgetfulFeatureDriver.Forgotten.Clear();
            (await s.RemoveTenantInstanceAsync( TestHelper.Monitor, acmeName )).ShouldBeTrue();
            ForgetfulFeatureDriver.Forgotten.ShouldBe( [acmeName] );
            s.TenantDomains.ShouldNotContain( t => t.FullName == acmeName );
            File.Exists( acmeLocal.AppendPart( "Instance.json" ) ).ShouldBeFalse();
            File.Exists( acmeLocal.AppendPart( "Secret.key" ) ).ShouldBeFalse();
            Directory.GetFiles( acmeLocal.AppendPart( "$TrashBin" ) ).ShouldNotBeEmpty( "The record is in the trash bin." );
            Directory.GetFiles( acmeLocal.AppendPart( "$TrashBin" ) ).ShouldAllBe( f => !File.ReadAllText( f ).Contains( "secret" ) );
        }
        // Restarted: nothing comes back.
        s = await StartAsync( "TTLife" );
        await using( s )
        {
            s.TenantDomains.ShouldHaveSingleItem().Configuration.Instance!.Name.ShouldBe( "Configured" );
            s.UnloadedTenants.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task a_runtime_tenant_that_the_configuration_contradicts_is_kept_unloaded_Async()
    {
        Clear( "TTUnloaded" );
        NormalizedPath acmeName;
        var s = await StartAsync( "TTUnloaded", maxInstanceCount: 4 );
        await using( s )
        {
            var template = s.TenantTemplates[0];
            (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "Acme" )).ShouldNotBeNull().Configuration.Instance!.Index.ShouldBe( 2 );
            var beta = (await s.AddTenantInstanceAsync( TestHelper.Monitor, template, "Beta" )).ShouldNotBeNull();
            beta.Configuration.Instance!.Index.ShouldBe( 3 );
            acmeName = template.GetFullName( "Acme" );
        }
        // The bound is lowered: Beta (3) is above it.
        s = await StartAsync( "TTUnloaded", maxInstanceCount: 2 );
        await using( s )
        {
            var u = s.UnloadedTenants.ShouldHaveSingleItem();
            u.Name.ShouldBe( "Beta" );
            u.Reason.ShouldBe( UnloadedTenantReason.IndexAboveBound );
            u.Index.ShouldBe( 3 );
        }
        // A configured instance is added: it takes the index 2 of Acme.
        s = await StartAsync( "TTUnloaded", c => c["Parties:0:Instances:1"] = "Second", maxInstanceCount: 4 );
        await using( s )
        {
            s.UnloadedTenants.Single( u => u.Name == "Acme" ).Reason.ShouldBe( UnloadedTenantReason.IndexTaken );
            s.TenantDomains.Single( t => t.Configuration.Instance?.Name == "Beta" ).Configuration.Instance!.Index.ShouldBe( 3 );
            // The operator moves Acme to a free index: it is loaded, with its keys.
            (await s.SetTenantIndexAsync( TestHelper.Monitor, acmeName, 4 )).ShouldBeTrue();
            s.UnloadedTenants.ShouldBeEmpty();
            s.TenantDomains.Single( t => t.FullName == acmeName ).Configuration.Instance!.Index.ShouldBe( 4 );
        }
    }

    [Test]
    public async Task concurrent_creations_take_distinct_indices_Async()
    {
        Clear( "TTConcurrent" );
        var s = await StartAsync( "TTConcurrent", maxInstanceCount: 6 );
        await using( s )
        {
            var template = s.TenantTemplates[0];
            var created = await Task.WhenAll( new[] { "A", "B", "C", "D", "E" }.Select( n => s.AddTenantInstanceAsync( TestHelper.Monitor, template, n ) ) );
            created.ShouldAllBe( t => t != null );
            created.Select( t => t!.Configuration.Instance!.Index ).OrderBy( i => i ).ShouldBe( [2, 3, 4, 5, 6] );
        }
    }

    [Test]
    public async Task the_pinned_remote_instances_of_a_runtime_tenant_are_found_after_a_restart_and_trashed_by_its_removal_Async()
    {
        Clear( "TTPinned" );
        static void WithOpenedRemoteTemplate( MutableConfigurationSection c )
        {
            c["Parties:0:Parties:0:PartyName"] = "$Worker-{Name}";
            c["Parties:0:Parties:0:InstanceCount"] = "0";
            c["Parties:0:Parties:0:MaxInstanceCount"] = "2";
        }
        NormalizedPath workerFolder;
        NormalizedPath acmeName;
        var s = await StartAsync( "TTPinned", WithOpenedRemoteTemplate );
        await using( s )
        {
            var acme = (await s.AddTenantInstanceAsync( TestHelper.Monitor, s.TenantTemplates[0], "Acme" )).ShouldNotBeNull();
            acmeName = acme.FullName;
            // The approval of "$Worker-1" pinned its identity in its folder of the store.
            workerFolder = ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.Combine( "#Dev/TTPinned/Customers/Acme/$Worker-1" );
            Directory.CreateDirectory( workerFolder );
            File.WriteAllBytes( workerFolder.AppendPart( "Identity.0.trust" ), [1] );
        }
        s = await StartAsync( "TTPinned", WithOpenedRemoteTemplate );
        await using( s )
        {
            var acme = s.TenantDomains.Single( t => t.FullName == acmeName );
            acme.Remotes.ShouldHaveSingleItem().PartyName.ShouldBe( "$Worker-1" );
            (await s.RemoveTenantInstanceAsync( TestHelper.Monitor, acmeName )).ShouldBeTrue();
            File.Exists( workerFolder.AppendPart( "Identity.0.trust" ) ).ShouldBeFalse( "A new Acme must not bring it back." );
        }
    }
}
