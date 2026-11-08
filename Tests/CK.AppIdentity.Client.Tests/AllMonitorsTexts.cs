using CK.Core;
using CK.Monitoring;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Client.Tests;

/// <summary>
/// Collects the texts written by any monitor (the agent of the application identity service has its own): a memory
/// collector on the <see cref="GrandOutput.Default"/>'s sink.
/// </summary>
sealed class AllMonitorsTexts : IDisposable
{
    readonly GrandOutputMemoryCollector _collector;
    readonly List<string> _all;

    public AllMonitorsTexts()
    {
        // The test monitor activates the default GrandOutput.
        TestHelper.Monitor.Trace( "Collecting the logs of all the monitors." );
        _collector = GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 1000 );
        _all = new List<string>();
    }

    /// <summary>
    /// Waits for a text that satisfies the predicate (the GrandOutput dispatches the logs asynchronously).
    /// </summary>
    public async Task<bool> WaitForAsync( Func<string, bool> predicate )
    {
        for( int i = 0; i < 50; ++i )
        {
            _all.AddRange( _collector.ExtractCurrentTexts() );
            if( _all.Any( predicate ) ) return true;
            await Task.Delay( 20 );
        }
        TestContext.Progress.WriteLine( "COLLECTED:" + Environment.NewLine + string.Join( Environment.NewLine + "---" + Environment.NewLine, _all ) );
        return false;
    }

    /// <summary>
    /// Waits for a text.
    /// </summary>
    public Task<bool> WaitForAsync( string text ) => WaitForAsync( t => t == text );

    public void Dispose() => _collector.Dispose();
}
