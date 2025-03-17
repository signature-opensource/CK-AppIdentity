using CK.Core;
using Shouldly;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class DynamicRemoteTests
{
    [Test]
    public async Task creating_and_destroying_dynamic_remote_or_tenants_raise_AllPartyChanged_event_Async()
    {
        var config = new MutableConfigurationSection( "DontCare" );
        config["CK-AppIdentity:FullName"] = "OneCS-SaaS/$OneCS1";

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Development } );
        builder.Configuration.Sources.Add( new ChainedConfigurationSource { Configuration = config } );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        using var app = builder.AddApplicationIdentityServiceConfiguration()
                               .CKBuild();

        await app.StartAsync();
        var s = app.Services.GetRequiredService<ApplicationIdentityService>();

        var events = new List<string>();
        s.AllPartyChanged.Sync += ( m, r ) =>
        {
            bool appear = !r.IsDestroyed;
            string msg;
            if( appear )
            {
                msg = $"'{r}' appeared.";
                r.ApplicationIdentityService.AllParties.ShouldContain( r, msg );
            }
            else
            {
                msg = $"'{r}' disappeared.";
                r.ApplicationIdentityService.AllParties.ShouldNotContain( r, msg );
            }
            m.Trace( msg );
            events.Add( msg );
        };
        Throw.DebugAssert( s != null );
        s.Parties.ShouldBeEmpty();

        // Adding a simple remote. This is a RemoteParty.
        IReadOnlyCollection<IRemoteParty>? addedRemotes = await s.AddMultipleRemotesAsync( TestHelper.Monitor, c =>
        {
            c["PartyName"] = "LogTower";
        } );
        Throw.DebugAssert( addedRemotes != null );
        var logTower = addedRemotes.Single();
        logTower.DomainName.ShouldBe( s.DomainName );
        logTower.EnvironmentName.ShouldBe( s.EnvironmentName );
        logTower.PartyName.ShouldBe( "$LogTower" );
        s.Parties.Single().ShouldBeSameAs( logTower );
        logTower.IsDynamic.ShouldBeTrue( "This remote is dynamic." );

        // Adding a tenant domain with an initial configured remote.
        AddedDynamicParties? added = await s.AddPartiesAsync( TestHelper.Monitor, c =>
        {
            c["DomainName"] = "LaToulousaine";
            c["PartyName"] = "$LaToulousaine";
            c["EnvironmentName"] = "#Debug";
            c["Parties:0:PartyName"] = "SignatureBox";
        } );
        Throw.DebugAssert( added != null );
        var laToulousaine = added.Value.Tenants.Single();
        laToulousaine.IsDynamic.ShouldBeTrue();
        var signatureBox = laToulousaine.Remotes.Single();
        signatureBox.FullName.ShouldBe( "LaToulousaine/$SignatureBox/#Debug" );
        signatureBox.IsDynamic.ShouldBeTrue( "The signatureBox has been dynamically added by its tenant." );

        // Adding a new dynamic remote to the domain.
        addedRemotes = await laToulousaine.AddMultipleRemotesAsync( TestHelper.Monitor, c =>
        {
            c["PartyName"] = "Trolley1";
        } );
        Throw.DebugAssert( addedRemotes != null );
        var theTrolley = addedRemotes.Single();
        theTrolley.FullName.ShouldBe( "LaToulousaine/$Trolley1/#Debug" );
        theTrolley.IsDynamic.ShouldBeTrue();
        laToulousaine.Remotes.Count.ShouldBe( 2 );

        // Adding another new dynamic remote to a dynamic domain.
        addedRemotes = await laToulousaine.AddMultipleRemotesAsync( TestHelper.Monitor, c =>
        {
            c["PartyName"] = "Trolley2";
        } );
        Throw.DebugAssert( addedRemotes != null );
        var theTrolley2 = addedRemotes.Single();
        theTrolley2.FullName.ShouldBe( "LaToulousaine/$Trolley2/#Debug" );
        theTrolley2.IsDynamic.ShouldBeTrue();
        laToulousaine.Remotes.Count.ShouldBe( 3 );

        // Destroying dynamic remotes.
        s.Parties.Count().ShouldBe( 2, "The LogTower and the LaToulousaine." );
        logTower.IsDestroyed.ShouldBeFalse();
        // The destruction is a background process that can be initiated by the
        // synchronous SetDestroyed().
        logTower.SetDestroyed();
        // To wait for the actual destruction of a remote, DestroyAsync() can always be called.
        await logTower.DestroyAsync();
        s.Parties.Count().ShouldBe( 1, "LaToulousaine only." );
        // Even when it's done of course.
        await logTower.DestroyAsync();

        // Destroying the dynamic Trolley.
        await theTrolley.DestroyAsync();

        // Destroying the tenant domain "OneCS-SaaS/$LaToulousaine/#Debug"
        // with "LaToulousaine/$SignatureBox/#Debug" and "LaToulousaine/$Trolley2/#Debug" in it.
        await laToulousaine.DestroyAsync();
        s.Parties.ShouldBeEmpty();

        events.ShouldBe( new string[]
        {
            "'OneCS-SaaS/$LogTower/#Dev' appeared.",

            // A group appears and its initially defined
            // remotes also appear in the events (as if it was dynamically added):
            // the event unifies the behavior.
            "'LaToulousaine/$LaToulousaine/#Debug' appeared.",
            "'LaToulousaine/$SignatureBox/#Debug' appeared.",

            "'LaToulousaine/$Trolley1/#Debug' appeared.",
            "'LaToulousaine/$Trolley2/#Debug' appeared.",

            "'OneCS-SaaS/$LogTower/#Dev' disappeared.",

            "'LaToulousaine/$Trolley1/#Debug' disappeared.",

            // When a group is destroyed, its destroyed remotes
            // appear before it.
            "'LaToulousaine/$SignatureBox/#Debug' disappeared.",
            "'LaToulousaine/$Trolley2/#Debug' disappeared.",
            "'LaToulousaine/$LaToulousaine/#Debug' disappeared."
        } );
    }
}
