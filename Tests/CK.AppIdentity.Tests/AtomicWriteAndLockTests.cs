using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Diagnostics;
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

    // Another process that holds the lock: on Unix the flock command (util-linux) takes the same flock(2)
    // as FileLock, on Windows a PowerShell opens the file with FileShare.None.
    static Process StartLockHolder( string path )
    {
        var info = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo( "powershell.exe", $"-NoProfile -NonInteractive -Command \"$f = [IO.File]::Open('{path}', 'OpenOrCreate', 'Read', 'None'); Start-Sleep 60\"" )
                    : new ProcessStartInfo( "flock", $"-x \"{path}\" sleep 60" );
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        try
        {
            return Process.Start( info ) ?? throw new InvalidOperationException( "Unable to start the lock holder." );
        }
        catch( System.ComponentModel.Win32Exception ) when( !OperatingSystem.IsWindows() )
        {
            Assert.Ignore( "The 'flock' command (util-linux) is not available." );
            throw;
        }
    }

    // Tries to take the lock from another process: true if it succeeded.
    static bool TryLockFromAnotherProcess( string path )
    {
        var info = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo( "powershell.exe", $"-NoProfile -NonInteractive -Command \"try {{ [IO.File]::Open('{path}', 'OpenOrCreate', 'Read', 'None').Dispose(); exit 0 }} catch {{ exit 1 }}\"" )
                    : new ProcessStartInfo( "flock", $"-n -x \"{path}\" true" );
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using var p = Process.Start( info ).ShouldNotBeNull();
        p.WaitForExit( 30_000 ).ShouldBeTrue();
        return p.ExitCode == 0;
    }

    [Test]
    public async Task the_lock_excludes_other_processes_Async()
    {
        var path = ApplicationIdentityServiceConfiguration.DefaultStoreRootPath.AppendPart( $"{Guid.NewGuid()}.lock" );
        // Created first: the external processes open it as is.
        using( FileLock.TryAcquire( path ) ) { }
        try
        {
            using( var held = FileLock.TryAcquire( path ) )
            {
                held.ShouldNotBeNull();
                TryLockFromAnotherProcess( path ).ShouldBeFalse( "We hold the lock." );
            }
            TryLockFromAnotherProcess( path ).ShouldBeTrue( "The lock has been released." );

            using var holder = StartLockHolder( path );
            try
            {
                // Waits for the holder to actually hold the lock.
                var start = Stopwatch.GetTimestamp();
                while( FileLock.TryAcquire( path ) is FileLock l )
                {
                    l.Dispose();
                    Stopwatch.GetElapsedTime( start ).ShouldBeLessThan( TimeSpan.FromSeconds( 20 ), "The lock holder process didn't take the lock." );
                    await Task.Delay( 50 );
                }
                (await FileLock.TryAcquireAsync( path, TimeSpan.FromMilliseconds( 200 ) )).ShouldBeNull( "Another process holds the lock." );
            }
            finally
            {
                holder.Kill( entireProcessTree: true );
                await holder.WaitForExitAsync();
            }
            using var afterKill = await FileLock.TryAcquireAsync( path, TimeSpan.FromSeconds( 5 ) );
            afterKill.ShouldNotBeNull( "The lock of a dead process is released by the OS." );
        }
        finally
        {
            File.Delete( path );
        }
    }

    static ApplicationIdentityService CreateService()
    {
        var config = ApplicationIdentityServiceConfiguration.CreateEmpty( "Test", "AtomicAndLock" );
        return new ApplicationIdentityService( config, new ServiceCollection().BuildServiceProvider() );
    }
}
