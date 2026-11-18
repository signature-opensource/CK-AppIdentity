using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

// A local party runs in one process at a time: it holds a lock on its private store ("-Local/$Lock.Party") while it runs.
// The lock is held by a handle: two services of this process are two processes for it.
[TestFixture]
public class PartyLockTests
{
    static readonly TimeSpan _timeout = TimeSpan.FromSeconds( 5 );

    static ApplicationIdentityService CreateService( string fullName, Action<MutableConfigurationSection>? configure = null )
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = fullName;
            configure?.Invoke( c );
        } );
        Throw.DebugAssert( config != null );
        var services = new ServiceCollection();
        services.AddSingleton( config );
        services.AddSingleton<ApplicationIdentityService>();
        return services.BuildServiceProvider().GetRequiredService<ApplicationIdentityService>();
    }

    [Test]
    public async Task the_same_party_can_not_run_twice_Async()
    {
        var first = CreateService( "Lock/$Twice" );
        await using( first )
        {
            await first.StartAndInitializeAsync().WaitAsync( _timeout );

            var second = CreateService( "Lock/$Twice" );
            await using( second )
            {
                var ex = await Should.ThrowAsync<Exception>( () => second.StartAndInitializeAsync().WaitAsync( _timeout ) );
                ex.Message.ShouldContain( "already running in another process" );
            }
        }
        // Once the first one is stopped, the party can run again.
        var third = CreateService( "Lock/$Twice" );
        await using( third )
        {
            await third.StartAndInitializeAsync().WaitAsync( _timeout );
        }
    }

    [Test]
    public async Task a_configured_tenant_is_a_party_of_its_own_and_a_failed_start_releases_its_locks_Async()
    {
        var first = CreateService( "Lock/$HolderA", c => c["Parties:0:FullName"] = "LockTenant/$LockTenant" );
        await using( first )
        {
            await first.StartAndInitializeAsync().WaitAsync( _timeout );

            // Another application with the same tenant: it fails to start.
            var second = CreateService( "Lock/$HolderB", c => c["Parties:0:FullName"] = "LockTenant/$LockTenant" );
            await using( second )
            {
                await Should.ThrowAsync<Exception>( () => second.StartAndInitializeAsync().WaitAsync( _timeout ) );
            }
            // Its own root has been released by its failed start: it runs without the tenant.
            var again = CreateService( "Lock/$HolderB" );
            await using( again )
            {
                await again.StartAndInitializeAsync().WaitAsync( _timeout );
            }
        }
    }

    [Test]
    public async Task a_dynamic_tenant_held_by_another_application_is_not_created_Async()
    {
        var first = CreateService( "Lock/$DynamicA" );
        var second = CreateService( "Lock/$DynamicB" );
        await using( first )
        await using( second )
        {
            await first.StartAndInitializeAsync().WaitAsync( _timeout );
            await second.StartAndInitializeAsync().WaitAsync( _timeout );

            var t = await first.AddTenantDomainAsync( TestHelper.Monitor, c => c["FullName"] = "LockDynamic/$LockDynamic" ).WaitAsync( _timeout );
            t.ShouldNotBeNull();

            (await second.AddTenantDomainAsync( TestHelper.Monitor, c => c["FullName"] = "LockDynamic/$LockDynamic" ).WaitAsync( _timeout ))
                .ShouldBeNull();

            // Destroyed, it is released: the other application can create it.
            await t.DestroyAsync().WaitAsync( _timeout );
            var t2 = await second.AddTenantDomainAsync( TestHelper.Monitor, c => c["FullName"] = "LockDynamic/$LockDynamic" ).WaitAsync( _timeout );
            t2.ShouldNotBeNull();
            await t2.DestroyAsync().WaitAsync( _timeout );
        }
    }
}
