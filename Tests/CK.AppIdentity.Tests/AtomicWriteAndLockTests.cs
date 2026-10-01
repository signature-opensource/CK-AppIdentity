using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class AtomicWriteAndLockTests
{
    [Test]
    public async Task a_failed_atomic_write_leaves_the_target_untouched_Async()
    {
        await using var s = CreateService();
        var store = s.SharedFileStore;
        var file = store.FolderPath.Combine( $"{Guid.NewGuid()}/Data.bin" );
        store.WriteAllBytes( file, new byte[] { 1 } );

        Should.Throw<InvalidOperationException>( () => store.WriteAtomically( file, f =>
        {
            f.WriteByte( 2 );
            throw new InvalidOperationException( "Crash in the middle." );
        } ) );
        await Should.ThrowAsync<InvalidOperationException>( () => store.WriteAtomicallyAsync( file, async ( f, cancel ) =>
        {
            await f.WriteAsync( new byte[] { 3 }, cancel );
            throw new InvalidOperationException( "Crash in the middle." );
        } ) );

        File.ReadAllBytes( file ).ShouldBe( new byte[] { 1 } );
        Directory.GetFiles( file.RemoveLastPart() ).Length.ShouldBe( 1, "No temporary file is left." );

        await store.WriteAtomicallyAsync( file, ( f, cancel ) => f.WriteAsync( new byte[] { 4 }, cancel ).AsTask() );
        File.ReadAllBytes( file ).ShouldBe( new byte[] { 4 } );
        Directory.Delete( file.RemoveLastPart(), recursive: true );
    }

    [Test]
    public async Task an_open_reader_does_not_prevent_an_atomic_replace_Async()
    {
        await using var s = CreateService();
        var store = s.SharedFileStore;
        var file = store.FolderPath.AppendPart( $"{Guid.NewGuid()}.bin" );
        store.WriteAllBytes( file, new byte[] { 1 } );

        using( var reader = store.OpenReadStream( file ) )
        {
            store.WriteAllBytes( file, new byte[] { 2 } );
            reader.ReadByte().ShouldBe( 1, "The reader still sees the file it opened." );
        }
        File.ReadAllBytes( file ).ShouldBe( new byte[] { 2 } );
        File.Delete( file );
    }

    [Test]
    public async Task reserved_file_names_are_rejected_Async()
    {
        await using var s = CreateService();
        var store = s.SharedFileStore;
        Should.Throw<ArgumentException>( () => store.WriteAllBytes( store.FolderPath.AppendPart( "$Tmp.x" ), new byte[] { 1 } ) );
        Should.Throw<ArgumentException>( () => store.OpenWriteStream( store.FolderPath.AppendPart( "$lock.x" ) ) );
        Should.Throw<ArgumentException>( () => store.TryAcquireLock( "../x" ) );
        Should.Throw<ArgumentException>( () => store.TryAcquireLock( "" ) );
    }

    [Test]
    public async Task old_temporary_files_are_swept_when_the_store_is_created_Async()
    {
        NormalizedPath folder;
        NormalizedPath oldTemp, recentTemp;
        await using( var s = CreateService() )
        {
            folder = s.SharedFileStore.FolderPath.AppendPart( $"{Guid.NewGuid()}" );
            Directory.CreateDirectory( folder );
            oldTemp = folder.AppendPart( "$Tmp.old" );
            recentTemp = folder.AppendPart( "$Tmp.recent" );
            File.WriteAllBytes( oldTemp, [1] );
            File.WriteAllBytes( recentTemp, [1] );
            File.SetLastWriteTimeUtc( oldTemp, DateTime.UtcNow.AddHours( -2 ) );
        }
        await using( var s = CreateService() )
        {
            File.Exists( oldTemp ).ShouldBeFalse();
            File.Exists( recentTemp ).ShouldBeTrue( "It may be an atomic write in progress in another process." );
        }
        Directory.Delete( folder, recursive: true );
    }

    [Test]
    public async Task a_store_lock_is_exclusive_until_disposed_Async()
    {
        await using var s = CreateService();
        var store = s.SharedFileStore;
        var name = Guid.NewGuid().ToString();

        var l1 = store.TryAcquireLock( name );
        l1.ShouldNotBeNull();
        store.TryAcquireLock( name ).ShouldBeNull( "Not reentrant, even in the same process." );
        (await store.TryAcquireLockAsync( name, TimeSpan.FromMilliseconds( 100 ) )).ShouldBeNull();

        // Released from another thread while a waiter polls.
        var waiter = store.TryAcquireLockAsync( name, TimeSpan.FromSeconds( 10 ) );
        await Task.Run( async () =>
        {
            await Task.Delay( 100 );
            l1.Dispose();
        } );
        using var l2 = await waiter;
        l2.ShouldNotBeNull();
        File.Exists( l2.Path ).ShouldBeTrue();
        l2.Path.LastPart.ShouldBe( "$Lock." + name );
    }

    [Test]
    public async Task waiting_for_a_lock_can_be_canceled_Async()
    {
        var path = ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.AppendPart( $"{Guid.NewGuid()}.lock" );
        using( var held = FileLock.TryAcquire( path ) )
        {
            held.ShouldNotBeNull();
            using var cts = new CancellationTokenSource( 100 );
            await Should.ThrowAsync<OperationCanceledException>( () => FileLock.TryAcquireAsync( path, Timeout.InfiniteTimeSpan, cancel: cts.Token ) );
        }
        using( var again = FileLock.TryAcquire( path ) )
        {
            again.ShouldNotBeNull();
        }
        File.Delete( path );
    }

    static ApplicationIdentityService CreateService()
    {
        var config = ApplicationIdentityServiceConfiguration.CreateEmpty( "Test", "AtomicAndLock" );
        return new ApplicationIdentityService( config, new ServiceCollection().BuildServiceProvider() );
    }
}
