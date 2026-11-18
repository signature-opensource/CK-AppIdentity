using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class OpenedTemplateTests
{
    // The folder of the instances in the store: {StoreRoot}/{Environment}/{Domain}.
    static NormalizedPath _fleetFolder => ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.Combine( "#Dev/OpenedFleet" );

    static IHost CreateHost( string maxInstanceCount = "4" )
    {
        var config = new MutableConfigurationSection( "DontCare" );
        config["CK-AppIdentity:FullName"] = "OpenedFleet/$Auth3";
        config["CK-AppIdentity:Parties:0:PartyName"] = "$Worker-{Name}";
        config["CK-AppIdentity:Parties:0:InstanceCount"] = "2";
        config["CK-AppIdentity:Parties:0:MaxInstanceCount"] = maxInstanceCount;
        config["CK-AppIdentity:Parties:0:Address"] = "tcp:127.0.0.1:{37300+Index}";
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Development } );
        builder.Configuration.Sources.Add( new Microsoft.Extensions.Configuration.ChainedConfigurationSource { Configuration = config } );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        return builder.AddApplicationIdentityServiceConfiguration().CKBuild();
    }

    static void Pin( IRemoteParty remote ) => File.WriteAllText( remote.SharedFileStore.FolderPath.AppendPart( "Identity.0.trust" ), "pinned" );

    [Test]
    public void an_Opened_remote_template_is_kept_on_its_local_party()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "OpenedFleet/$Auth3";
            c["Parties:0:PartyName"] = "Worker-{Name}";
            c["Parties:0:InstanceCount"] = "0";
            c["Parties:0:MaxInstanceCount"] = "3";
            c["Parties:1:PartyName"] = "Cart-{Index}";
            c["Parties:1:InstanceCount"] = "2";
        } ).ShouldNotBeNull();
        config.Remotes.Select( r => r.PartyName ).ShouldBe( ["$Cart-1", "$Cart-2"] );
        config.Remotes.All( r => r.Template != null && !r.IsDynamicInstance ).ShouldBeTrue();
        var t = config.RemoteTemplates.ShouldHaveSingleItem();
        t.PartyNamePattern.ShouldBe( "$Worker-{Name}" );
        t.MatchDynamic( "OpenedFleet/$Worker-3/#Dev" ).ShouldNotBeNull().Index.ShouldBe( 3 );
        t.MatchDynamic( "OpenedFleet/$Worker-4/#Dev" ).ShouldBeNull();
        var w2 = t.CreateInstance( TestHelper.Monitor, t.Instances.Find( "2" )! ).ShouldNotBeNull();
        w2.FullName.Path.ShouldBe( "OpenedFleet/$Worker-2/#Dev" );
        w2.IsDynamicInstance.ShouldBeTrue();
    }

    [TestCase( "\"PartyName\": \"Worker\"", "must use the \"{Name}\" or \"{Index}\" placeholder" )]
    // An Opened tenant template is written with a "DomainName" and the "PartyName" "${Name}" (UC 4.3), not a "FullName".
    [TestCase( "\"FullName\": \"Shop-{Name}/$Shop-{Name}\"", "must have a \"DomainName\" that ends with \"/{Name}\"" )]
    public void invalid_Opened_templates_are_errors( string party, string expected )
    {
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "OpenedFleet/$Auth3";
                var kv = party.Split( ':', 2 );
                c[$"Parties:0:{kv[0].Trim().Trim( '"' )}"] = kv[1].Trim().Trim( '"' );
                c["Parties:0:InstanceCount"] = "1";
                c["Parties:0:MaxInstanceCount"] = "3";
            } ).ShouldBeNull();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public async Task a_dynamic_instance_is_created_again_at_start_when_it_is_pinned_Async()
    {
        if( Directory.Exists( _fleetFolder ) ) Directory.Delete( _fleetFolder, true );
        using( var app = CreateHost() )
        {
            await app.StartAsync();
            var s = app.Services.GetRequiredService<ApplicationIdentityService>();
            s.Remotes.Select( r => r.PartyName ).ShouldBe( ["$Worker-1", "$Worker-2"] );
            // A configured instance, an instance out of the bound, another domain: not candidates.
            (await s.TryAddTemplateInstanceAsync( TestHelper.Monitor, "OpenedFleet/$Worker-2/#Dev" )).ShouldBeNull();
            (await s.TryAddTemplateInstanceAsync( TestHelper.Monitor, "OpenedFleet/$Worker-5/#Dev" )).ShouldBeNull();
            (await s.TryAddTemplateInstanceAsync( TestHelper.Monitor, "Other/$Worker-3/#Dev" )).ShouldBeNull();

            var w3 = (await s.TryAddTemplateInstanceAsync( TestHelper.Monitor, "OpenedFleet/$Worker-3/#Dev" )).ShouldNotBeNull();
            w3.IsDynamic.ShouldBeTrue();
            w3.Configuration.IsDynamicInstance.ShouldBeTrue();
            w3.Configuration.Address.ShouldBe( "tcp:127.0.0.1:37303" );
            (await s.TryAddTemplateInstanceAsync( TestHelper.Monitor, "OpenedFleet/$Worker-3/#Dev" )).ShouldBeNull( "Already a remote." );
            _ = (await s.TryAddTemplateInstanceAsync( TestHelper.Monitor, "OpenedFleet/$Worker-4/#Dev" )).ShouldNotBeNull();
            // The approval pins $Worker-3: $Worker-4 stays a candidate (not pinned).
            Pin( w3 );
            await app.StopAsync();
        }
        // Simulates an instance above a bound that has been lowered.
        var w9 = _fleetFolder.AppendPart( "$Worker-9" );
        Directory.CreateDirectory( w9.AppendPart( "-Local" ) );
        File.WriteAllText( w9.AppendPart( "Identity.0.trust" ), "pinned" );
        using( var app = CreateHost() )
        {
            await app.StartAsync();
            var s = app.Services.GetRequiredService<ApplicationIdentityService>();
            s.Remotes.Select( r => r.PartyName ).ShouldBe( ["$Worker-1", "$Worker-2", "$Worker-3"], ignoreOrder: true );
            File.Exists( w9.AppendPart( "Identity.0.trust" ) ).ShouldBeFalse( "Above the bound: trashed." );
            Directory.Exists( w9.AppendPart( "-Local" ) ).ShouldBeTrue( "The folder is kept." );

            var w3 = s.Remotes.Single( r => r.PartyName == "$Worker-3" );
            w3.IsDynamic.ShouldBeTrue();
            (await s.RemoveTemplateInstanceAsync( TestHelper.Monitor, s.Remotes.Single( r => r.PartyName == "$Worker-1" ) )).ShouldBeFalse( "A configured instance." );
            (await s.RemoveTemplateInstanceAsync( TestHelper.Monitor, w3 )).ShouldBeTrue();
            w3.IsDestroyed.ShouldBeTrue();
            s.Remotes.ShouldNotContain( w3 );
            Directory.EnumerateFiles( w3.SharedFileStore.FolderPath, IFileStore.TrustFilePattern ).ShouldBeEmpty();
            await app.StopAsync();
        }
        using( var app = CreateHost() )
        {
            await app.StartAsync();
            var s = app.Services.GetRequiredService<ApplicationIdentityService>();
            s.Remotes.Select( r => r.PartyName ).ShouldBe( ["$Worker-1", "$Worker-2"], ignoreOrder: true );
            await app.StopAsync();
        }
    }
}
