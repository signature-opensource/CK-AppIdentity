using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class DevRunningStatusTests
{
    public sealed class TestContributor : IDevRunningStatusContributor
    {
        Action? _onChanged;

        public int ActivationCount { get; private set; }

        public bool IsReady { get; private set; }

        public void SetReady()
        {
            IsReady = true;
            _onChanged?.Invoke();
        }

        public void Activate( IActivityMonitor monitor, Action onChanged )
        {
            ++ActivationCount;
            _onChanged = onChanged;
        }

        public void Write( Utf8JsonWriter writer ) => writer.WriteString( "Test", IsReady ? "ready" : "not ready" );
    }

    // The "#Dev/Store" of the tests (the DefaultStoreRootPath is set by the tests: a host in Development doesn't use the
    // one of the solution).
    static NormalizedPath StoreFolder => TestHelper.TestProjectFolder.Combine( "TestStore/#Dev/Store" );

    // useDevStore: the configuration is created with the dev store, like a host in Development that runs from a solution.
    static IHost BuildHost( Action<MutableConfigurationSection>? configure = null, bool useDevStore = true )
    {
        var ckApp = new MutableConfigurationSection( "CK-AppIdentity" );
        ckApp["FullName"] = "D/$App/#Dev";
        ckApp["Parties:0:PartyName"] = "Incoming";
        ckApp["Parties:1:PartyName"] = "Outgoing";
        ckApp["Parties:1:Address"] = "tcp:127.0.0.1:1";
        configure?.Invoke( ckApp );
        var configuration = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor,
                                                                            ckApp,
                                                                            null,
                                                                            null,
                                                                            CoreApplicationIdentity.DefaultEnvironmentName,
                                                                            TestHelper.TestProjectFolder,
                                                                            useDevStore ? StoreFolder : default ).ShouldNotBeNull();
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = TestHelper.TestProjectFolder
        } );
        builder.Services.AddSingleton( configuration );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<DevRunningStatusFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<DevRunningStatusFeatureDriver>() );
        builder.Services.AddSingleton<TestContributor>();
        builder.Services.AddSingleton<IDevRunningStatusContributor>( sp => sp.GetRequiredService<TestContributor>() );
        return builder.CKBuild();
    }

    static JsonElement Read( NormalizedPath path ) => JsonDocument.Parse( File.ReadAllText( path ) ).RootElement.Clone();

    // The rewrites are batched: waits until the file says what is expected.
    static async Task<JsonElement> WaitForAsync( NormalizedPath path, Func<JsonElement, bool> condition )
    {
        for( int i = 0; i < 50; ++i )
        {
            var e = Read( path );
            if( condition( e ) ) return e;
            await Task.Delay( 100 );
        }
        return Read( path );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task the_file_exists_while_the_host_runs_Async( CancellationToken cancel )
    {
        using var host = BuildHost();
        await host.StartAsync( cancel );
        var driver = host.Services.GetRequiredService<DevRunningStatusFeatureDriver>();
        driver.IsActive.ShouldBeTrue();
        var path = driver.FilePath;
        path.Path.ShouldStartWith( StoreFolder.Path );
        path.LastPart.ShouldBe( DevRunningStatusFeatureDriver.FileName );

        var status = Read( path );
        status.GetProperty( "FullName" ).GetString().ShouldBe( "D/$App/#Dev" );
        using( var process = Process.GetCurrentProcess() )
        {
            status.GetProperty( "ProcessId" ).GetInt32().ShouldBe( process.Id );
        }
        status.GetProperty( "ContentRootPath" ).GetString().ShouldBe( TestHelper.TestProjectFolder.Path );
        var remotes = status.GetProperty( "Remotes" ).EnumerateArray().ToDictionary( r => r.GetProperty( "FullName" ).GetString()!,
                                                                                         r => r.GetProperty( "Outgoing" ).GetBoolean() );
        remotes["D/$Incoming/#Dev"].ShouldBeFalse();
        remotes["D/$Outgoing/#Dev"].ShouldBeTrue();
        status.GetProperty( "Test" ).GetString().ShouldBe( "not ready" );

        await host.StopAsync( cancel );
        File.Exists( path ).ShouldBeFalse();
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task Ready_follows_the_contributors_Async( CancellationToken cancel )
    {
        using var host = BuildHost();
        await host.StartAsync( cancel );
        var path = host.Services.GetRequiredService<DevRunningStatusFeatureDriver>().FilePath;
        Read( path ).GetProperty( "Ready" ).GetBoolean().ShouldBeFalse();

        host.Services.GetRequiredService<TestContributor>().SetReady();
        var status = await WaitForAsync( path, e => e.GetProperty( "Ready" ).GetBoolean() );
        status.GetProperty( "Ready" ).GetBoolean().ShouldBeTrue();
        status.GetProperty( "Test" ).GetString().ShouldBe( "ready" );
        await host.StopAsync( cancel );
    }

    [TestCase( "OtherEnvironment" )]
    [TestCase( "DefaultStore" )]
    [TestCase( "RelativeStore" )]
    [TestCase( "Disallowed" )]
    [CancelAfter( 10000 )]
    public async Task outside_a_development_solution_the_feature_is_dormant_Async( string reason, CancellationToken cancel )
    {
        using var host = BuildHost( c =>
        {
            switch( reason )
            {
                case "OtherEnvironment": c["FullName"] = "D/$App/#Staging"; break;
                // A configured store is not the dev store, even in the solution.
                case "RelativeStore": c["StoreRootPath"] = "TestStore/#Dev/Store"; break;
                case "Disallowed": c["Local:DisallowFeatures"] = "DevRunningStatus"; break;
            }
        }, useDevStore: reason != "DefaultStore" );
        await host.StartAsync( cancel );
        var driver = host.Services.GetRequiredService<DevRunningStatusFeatureDriver>();
        driver.IsActive.ShouldBeFalse();
        driver.FilePath.IsEmptyPath.ShouldBeTrue();
        host.Services.GetRequiredService<TestContributor>().ActivationCount.ShouldBe( 0 );
        if( Directory.Exists( StoreFolder ) )
        {
            Directory.EnumerateFiles( StoreFolder, DevRunningStatusFeatureDriver.FileName, SearchOption.AllDirectories ).ShouldBeEmpty();
        }
        await host.StopAsync( cancel );
    }

    [TestCase( true, false, true )]
    [TestCase( false, false, false )]
    [TestCase( true, true, false )]
    public void the_dev_store_is_used_in_Development_unless_the_DefaultStoreRootPath_is_set( bool isDevelopment, bool isDefaultStoreRootPathSet, bool expected )
    {
        var solution = new NormalizedPath( "C:/Dev/MySolution" );
        var p = ApplicationIdentityServiceConfiguration.GetDevStoreRootPath( isDevelopment, isDefaultStoreRootPathSet, solution );
        p.ShouldBe( expected ? solution.Combine( "#Dev/Store" ) : default );
        // Not in a solution (a deployed application): no dev store.
        ApplicationIdentityServiceConfiguration.GetDevStoreRootPath( isDevelopment, isDefaultStoreRootPathSet, default ).IsEmptyPath.ShouldBeTrue();
    }

    [Test]
    public void the_dev_store_creates_the_Dev_folder_and_a_configured_store_wins()
    {
        var devFolder = TestHelper.TestProjectFolder.Combine( "TestStore/Solution/#Dev" );
        TestHelper.CleanupFolder( devFolder.RemoveLastPart(), ensureFolderAvailable: false );
        var ckApp = new MutableConfigurationSection( "CK-AppIdentity" );
        ckApp["FullName"] = "D/$App/#Dev";
        var c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, ckApp, null, null, "#Dev", TestHelper.TestProjectFolder, devFolder.AppendPart( "Store" ) ).ShouldNotBeNull();
        c.IsDevStore.ShouldBeTrue();
        c.IsPrivateStore.ShouldBeTrue();
        c.IsRelativeStore.ShouldBeFalse();
        c.StoreRootPath.ShouldBe( devFolder.AppendPart( "Store" ) );
        // Its content is ignored by git.
        File.ReadAllText( devFolder.AppendPart( ".gitignore" ) ).Trim().ShouldBe( "*" );

        ckApp["StoreRootPath"] = "TestStore/Other";
        c = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, ckApp, null, null, "#Dev", TestHelper.TestProjectFolder, devFolder.AppendPart( "Store" ) ).ShouldNotBeNull();
        c.IsDevStore.ShouldBeFalse();
        c.IsRelativeStore.ShouldBeTrue();
    }
}
