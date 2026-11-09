using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class PartyDescriptionTests
{
    // Contributes to the root party's description, once it has set it up: the description driver, that comes first,
    // describes the parties once all the drivers have set them up.
    public sealed class TestContributorFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        bool _isSetup;

        public TestContributorFeatureDriver( ApplicationIdentityService s ) : base( s, null, true ) { }

        public Uri? PublicUrl { get; set; }

        public string[] Applications { get; set; } = ["App:/"];

        public Task ChangeAsync( Uri? publicUrl, params string[] applications )
        {
            PublicUrl = publicUrl;
            Applications = applications;
            return OnDescriptionChangedAsync( TestHelper.Monitor, ApplicationIdentityService );
        }

        protected override Task<bool> SetupAsync( FeatureLifetimeContext context )
        {
            _isSetup = true;
            return Task.FromResult( true );
        }

        protected override void ContributeDescription( ILocalParty party, PartyDescription.Builder builder )
        {
            if( party != ApplicationIdentityService || !_isSetup ) return;
            builder.PublicUrl ??= PublicUrl;
            foreach( var a in Applications )
            {
                var parts = a.Split( ':' );
                builder.AddClientApplication( parts[0], parts[1] );
            }
        }
    }

    static IHost BuildHost( Action<MutableConfigurationSection>? configure = null, params Type[] drivers )
    {
        var ckApp = new MutableConfigurationSection( "CK-AppIdentity" );
        ckApp["FullName"] = "Descr/$App";
        ckApp["Parties:0:PartyName"] = "Remote";
        configure?.Invoke( ckApp );
        var configuration = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, ckApp ).ShouldNotBeNull();
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Services.AddSingleton( configuration );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<PartyDescriptionFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<PartyDescriptionFeatureDriver>() );
        builder.Services.AddSingleton<TestContributorFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<TestContributorFeatureDriver>() );
        foreach( var t in drivers )
        {
            builder.Services.AddSingleton( t );
            builder.Services.AddSingleton( sp => (IApplicationIdentityFeatureDriver)sp.GetRequiredService( t ) );
        }
        return builder.CKBuild();
    }

    [TestCase( "https://acme.com", true )]
    [TestCase( "http://localhost:5300/", true )]
    [TestCase( "ftp://acme.com", false )]
    [TestCase( "/relative", false )]
    [TestCase( "https://acme.com/path", false )]
    [TestCase( "https://acme.com?q=1", false )]
    [TestCase( "https://acme.com#f", false )]
    [TestCase( "https://user@acme.com", false )]
    public void the_public_URL_is_an_http_or_https_origin( string value, bool isValid )
    {
        PartyDescription.TryParsePublicUrl( value, out _, out _ ).ShouldBe( isValid );
    }

    [Test]
    public void the_URL_of_a_client_application_is_derived()
    {
        PartyDescription.TryParsePublicUrl( "http://localhost:5300", out var url, out _ ).ShouldBeTrue();
        var d = new PartyDescription( url, [new ClientApplicationDescription( "App", "/" ), new ClientApplicationDescription( "MiniApp", "/mini-app/" )] );
        d.GetClientApplicationUrl().ShouldBe( "http://localhost:5300" );
        d.GetClientApplicationUrl( "MiniApp" ).ShouldBe( "http://localhost:5300/mini-app" );
        d.GetClientApplicationUrl( "Unknown" ).ShouldBeNull();
        new PartyDescription( null, d.ClientApplications ).GetClientApplicationUrl().ShouldBeNull( "No public URL." );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_configured_public_URL_wins_and_the_contributions_complete_the_description_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Local:PublicUrl"] = "https://acme.com" );
        var contributor = host.Services.GetRequiredService<TestContributorFeatureDriver>();
        contributor.PublicUrl = new Uri( "http://localhost:5300" );
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        root.Description.PublicUrl.ShouldBe( new Uri( "https://acme.com" ) );
        root.Description.GetClientApplicationUrl().ShouldBe( "https://acme.com" );

        // A changed contribution changes the description, and DescriptionChanged is raised.
        var changed = new TaskCompletionSource<PartyDescription>( TaskCreationOptions.RunContinuationsAsynchronously );
        root.DescriptionChanged.Async += ( monitor, p, c ) => { changed.TrySetResult( p.Description ); return Task.CompletedTask; };
        await contributor.ChangeAsync( null, "App:/", "MiniApp:/mini-app" );
        var d = await changed.Task.WaitAsync( cancel );
        d.GetClientApplicationUrl( "MiniApp" ).ShouldBe( "https://acme.com/mini-app" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task without_configuration_the_contributed_public_URL_is_used_Async( CancellationToken cancel )
    {
        using var host = BuildHost();
        host.Services.GetRequiredService<TestContributorFeatureDriver>().PublicUrl = new Uri( "http://localhost:5300" );
        await host.StartAsync( cancel );
        host.Services.GetRequiredService<ApplicationIdentityService>().Description.GetClientApplicationUrl().ShouldBe( "http://localhost:5300" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task an_invalid_configured_public_URL_fails_the_start_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Local:PublicUrl"] = "https://acme.com/not-an-origin" );
        await Should.ThrowAsync<CKException>( () => host.StartAsync( cancel ) );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task the_description_of_a_remote_is_persisted_and_its_public_URL_change_is_a_warning_Async( CancellationToken cancel )
    {
        PartyDescription.TryParsePublicUrl( "https://remote.acme.com", out var url, out _ ).ShouldBeTrue();
        var announced = new PartyDescription( url, [new ClientApplicationDescription( "App", "/" )] );
        using( var host = BuildHost() )
        {
            await host.StartAsync( cancel );
            var remote = host.Services.GetRequiredService<ApplicationIdentityService>().Remotes.Single();
            // A fresh start.
            remote.SharedFileStore.TryTrash( TestHelper.Monitor, remote.SharedFileStore.FolderPath.AppendPart( "PartyDescription.json" ), immediateDelete: true );
            remote.SetDescription( TestHelper.Monitor, PartyDescription.Empty );

            remote.SetDescription( TestHelper.Monitor, announced ).ShouldBeTrue();
            remote.SetDescription( TestHelper.Monitor, announced ).ShouldBeFalse( "The same description." );
            await host.StopAsync( cancel );
        }
        using( var host = BuildHost() )
        {
            await host.StartAsync( cancel );
            var remote = host.Services.GetRequiredService<ApplicationIdentityService>().Remotes.Single();
            remote.Description.ShouldBe( announced, "Read back from the store." );

            PartyDescription.TryParsePublicUrl( "https://other.acme.com", out var other, out _ ).ShouldBeTrue();
            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                remote.SetDescription( TestHelper.Monitor, new PartyDescription( other, announced.ClientApplications ) ).ShouldBeTrue();
                entries.ShouldContain( e => e.Text.Contains( "public URL" ) && e.Text.Contains( "changed" ) );
            }
            await host.StopAsync( cancel );
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_caller_party_is_the_local_party_or_one_of_its_remotes_Async( CancellationToken cancel )
    {
        using var host = BuildHost();
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        var remote = root.Remotes.Single();
        // Null: unknown or not applicable.
        root.FindCallerParty( null ).ShouldBeNull();
        root.FindCallerParty( root.FullName ).ShouldBeSameAs( root );
        root.FindCallerParty( remote.FullName ).ShouldBeSameAs( remote );
        // The comparison is ordinal.
        root.FindCallerParty( remote.FullName.Path.ToUpperInvariant() ).ShouldBeNull();
        root.FindCallerParty( "Acme/$Unknown" ).ShouldBeNull();
        await host.StopAsync( cancel );
    }

    // The first phase hosts a route and announces a data, the second one derives a route from the first phase.
    public sealed class RoutingFeatureDriver : FeatureBuilderInitializationTests.NoopFeatureDriver
    {
        public RoutingFeatureDriver( ApplicationIdentityService s ) : base( s, null, true ) { }

        protected override void ContributeDescription( ILocalParty party, PartyDescription.Builder builder )
        {
            builder.HostRoute( "Hosted", "Some.Command" );
            builder.SetData( "Region", "eu-west" );
        }

        protected override void ContributeDerivedDescription( ILocalParty party, PartyDescription.DerivedBuilder builder )
        {
            foreach( var (name, target) in builder.HostedRoutes )
            {
                builder.SetRoute( name, PartyRoute.Hosted( target, party.FullName ) );
            }
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_second_phase_derives_from_the_first_one_Async( CancellationToken cancel )
    {
        using var host = BuildHost( null, typeof( RoutingFeatureDriver ) );
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        root.Description.Data["Region"].ShouldBe( "eu-west" );
        root.Description.Routes["Hosted"].ShouldBe( PartyRoute.Hosted( "Some.Command", root.FullName ) );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_changed_announcement_raises_RemoteDescriptionChanged_Async( CancellationToken cancel )
    {
        using var host = BuildHost();
        await host.StartAsync( cancel );
        var root = host.Services.GetRequiredService<ApplicationIdentityService>();
        var remote = root.Remotes.Single();
        var raised = new System.Collections.Concurrent.ConcurrentQueue<IRemoteParty>();
        var signal = new SemaphoreSlim( 0 );
        root.RemoteDescriptionChanged.Sync += ( monitor, r ) => { raised.Enqueue( r ); signal.Release(); };

        // A fresh start (a previous run persisted a description): this is a change too.
        remote.SharedFileStore.TryTrash( TestHelper.Monitor, remote.SharedFileStore.FolderPath.AppendPart( "PartyDescription.json" ), immediateDelete: true );
        if( remote.SetDescription( TestHelper.Monitor, PartyDescription.Empty ) )
        {
            (await signal.WaitAsync( 5000, cancel )).ShouldBeTrue();
        }
        raised.Clear();

        PartyDescription.TryParsePublicUrl( "https://remote.acme.com", out var url, out _ ).ShouldBeTrue();
        var announced = new PartyDescription( url, [new ClientApplicationDescription( "App", "/" )] );
        remote.SetDescription( TestHelper.Monitor, announced ).ShouldBeTrue();
        (await signal.WaitAsync( 5000, cancel )).ShouldBeTrue( "Raised by the agent's loop." );
        raised.ShouldHaveSingleItem().ShouldBeSameAs( remote );

        remote.SetDescription( TestHelper.Monitor, announced ).ShouldBeFalse( "The same description." );
        (await signal.WaitAsync( 300, cancel )).ShouldBeFalse( "Nothing changed: nothing is raised." );
        await host.StopAsync( cancel );
    }

    [Test]
    public void the_facts_of_a_client_application_are_a_trait_compared_and_persisted()
    {
        var facts = ClientApplicationDescription.FactContext.FindOrCreate( "Enlistment|Admin" );
        var app = new ClientApplicationDescription( "App", "/", facts );
        app.Facts.ToString().ShouldBe( "Admin|Enlistment", "A trait is sorted." );
        app.ShouldBe( new ClientApplicationDescription( "App", "/", ClientApplicationDescription.FactContext.FindOrCreate( "Admin|Enlistment" ) ) );
        app.ShouldNotBe( new ClientApplicationDescription( "App", "/" ), "The facts are compared." );
        new ClientApplicationDescription( "App", "/" ).Facts.IsEmpty.ShouldBeTrue();
        app.ToString().ShouldBe( "App (/, Facts: Admin|Enlistment)" );
        Should.Throw<ArgumentException>( () => new ClientApplicationDescription( "App", "/", CKTraitContext.Create( "Other" ).FindOrCreate( "X" ) ) );

        var d = new PartyDescription( null, [app, new ClientApplicationDescription( "MiniApp", "/mini-app" )] );
        using var stream = new MemoryStream();
        using( var w = new System.Text.Json.Utf8JsonWriter( stream ) ) d.Write( w );
        using var doc = System.Text.Json.JsonDocument.Parse( stream.ToArray() );
        PartyDescription.TryRead( doc.RootElement, out var back, out _ ).ShouldBeTrue();
        back.ShouldBe( d );
        back.ClientApplications[0].Facts.ShouldBeSameAs( facts );
    }

    [Test]
    public void the_roles_are_sorted_deduplicated_and_persisted()
    {
        var d = new PartyDescription( null, default, ["B", "A", "B"] );
        d.Roles.ShouldBe( ["A", "B"] );
        d.ToString().ShouldContain( "(Roles: A, B)" );
        using var stream = new MemoryStream();
        using( var w = new System.Text.Json.Utf8JsonWriter( stream ) ) d.Write( w );
        using var doc = System.Text.Json.JsonDocument.Parse( stream.ToArray() );
        PartyDescription.TryRead( doc.RootElement, out var back, out _ ).ShouldBeTrue();
        back.ShouldBe( d );
        back.ShouldNotBe( d.WithRoles( null ) );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_roles_of_a_local_party_are_the_asymmetric_features_it_is_Async( CancellationToken cancel )
    {
        // The Local side is installed and the root allows "Asym": it is the "Asym".
        using( var host = BuildHost( c => c["Local:AllowFeatures"] = "Asym", typeof( FeatureBuilderInitializationTests.LocalAsymFeatureDriver ) ) )
        {
            await host.StartAsync( cancel );
            host.Services.GetRequiredService<ApplicationIdentityService>().Description.Roles.ShouldBe( ["Asym"] );
            await host.StopAsync( cancel );
        }
        // Only the Remote side: the root cannot be the "Asym".
        using( var host = BuildHost( c => c["Local:AllowFeatures"] = "Asym", typeof( FeatureBuilderInitializationTests.RemoteAsymFeatureDriver ) ) )
        {
            await host.StartAsync( cancel );
            host.Services.GetRequiredService<ApplicationIdentityService>().Description.Roles.ShouldBeEmpty();
            await host.StopAsync( cancel );
        }
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task a_remote_to_which_a_feature_is_relayed_must_announce_it_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Parties:0:AllowFeatures"] = "AsymRemote", typeof( FeatureBuilderInitializationTests.RemoteAsymFeatureDriver ) );
        await host.StartAsync( cancel );
        var remote = host.Services.GetRequiredService<ApplicationIdentityService>().Remotes.Single();
        remote.SharedFileStore.TryTrash( TestHelper.Monitor, remote.SharedFileStore.FolderPath.AppendPart( "PartyDescription.json" ), immediateDelete: true );
        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            // The remote forgot the "Asym" of its "Local".
            remote.SetDescription( TestHelper.Monitor, new PartyDescription( null, default, ["Other"] ) ).ShouldBeTrue();
            entries.ShouldContain( e => e.MaskedLevel == LogLevel.Error
                                        && e.Text.Contains( "relays the 'Asym' feature to 'Descr/$Remote/#Dev' (it allows 'AsymRemote') but 'Descr/$Remote/#Dev' doesn't announce being the 'Asym'" ) );
        }
        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            remote.SetDescription( TestHelper.Monitor, new PartyDescription( null, default, ["Asym"] ) ).ShouldBeTrue();
            entries.ShouldBeEmpty();
        }
        await host.StopAsync( cancel );
    }
}
