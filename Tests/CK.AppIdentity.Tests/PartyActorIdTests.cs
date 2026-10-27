using CK.Core;
using CK.Testing;
using Shouldly;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class PartyActorIdTests
{
    [Test]
    public async Task PartyActorId_is_0_until_set_once_Async()
    {
        var config = new DynamicConfigurationSource();
        using( config.StartBatch() )
        {
            config["CK-AppIdentity:FullName"] = "Acme/$Front";
            config["CK-AppIdentity:Parties:0:PartyName"] = "AuthCenter";
        }
        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.Sources.Add( config );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        using var app = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await app.StartAsync();

        var root = app.Services.GetRequiredService<ApplicationIdentityService>();
        var remote = root.Remotes.Single();
        root.PartyActorId.ShouldBe( 0 );
        remote.PartyActorId.ShouldBe( 0 );

        Util.Invokable( () => root.TrySetPartyActorId( TestHelper.Monitor, 0 ) ).ShouldThrow<ArgumentOutOfRangeException>();

        root.TrySetPartyActorId( TestHelper.Monitor, 12 ).ShouldBeTrue();
        root.PartyActorId.ShouldBe( 12 );
        root.TrySetPartyActorId( TestHelper.Monitor, 12 ).ShouldBeTrue();
        root.TrySetPartyActorId( TestHelper.Monitor, 13 ).ShouldBeFalse();
        root.PartyActorId.ShouldBe( 12 );

        remote.TrySetPartyActorId( TestHelper.Monitor, 3712 ).ShouldBeTrue();
        remote.PartyActorId.ShouldBe( 3712 );
        root.PartyActorId.ShouldBe( 12 );

        await app.StopAsync();
    }
}
