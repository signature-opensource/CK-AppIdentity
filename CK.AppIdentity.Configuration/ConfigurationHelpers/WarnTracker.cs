using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// Counts the warnings emitted while a configuration is analyzed: in StrictConfigurationMode, no warning
/// must be emitted. While tracking, the monitor's minimal filter is combined with <see cref="LogFilter.Minimal"/>
/// so that warnings can't be filtered out before being counted: it is restored by <see cref="Dispose"/>.
/// </summary>
sealed class WarnTracker : IActivityMonitorClient, IDisposable
{
    readonly IActivityMonitorOutput _output;
    readonly IDisposable _filter;
    int _warnCount;
    bool _disposed;

    public WarnTracker( IActivityMonitor monitor )
    {
        _filter = monitor.TemporarilySetMinimalFilter( monitor.MinimalFilter.Combine( LogFilter.Minimal ) );
        _output = monitor.Output;
        _output.RegisterClient( this );
    }

    public int WarnCount => _warnCount;

    /// <summary>
    /// Stops counting and, when <paramref name="strictMode"/> is true and warnings have been emitted, emits an error.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="strictMode">Whether StrictConfigurationMode is true.</param>
    /// <returns>False if strict mode is on and warnings have been emitted.</returns>
    public bool Close( IActivityMonitor monitor, bool strictMode )
    {
        Dispose();
        if( strictMode && _warnCount > 0 )
        {
            monitor.Error( $"{_warnCount} warnings occurred and StrictConfigurationMode is true: no warning must be emitted." );
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if( !_disposed )
        {
            _disposed = true;
            _output.UnregisterClient( this );
            _filter.Dispose();
        }
    }

    public void OnUnfilteredLog( ref ActivityMonitorLogData data )
    {
        if( data.MaskedLevel == LogLevel.Warn ) _warnCount++;
    }

    public void OnOpenGroup( IActivityLogGroup group )
    {
        if( group.Data.MaskedLevel == LogLevel.Warn ) _warnCount++;
    }

    public void OnGroupClosing( IActivityLogGroup group, ref List<ActivityLogGroupConclusion>? conclusions )
    {
    }

    public void OnGroupClosed( IActivityLogGroup group, IReadOnlyList<ActivityLogGroupConclusion> conclusions )
    {
    }

    public void OnTopicChanged( string newTopic, string? fileName, int lineNumber )
    {
    }

    public void OnAutoTagsChanged( CKTrait newTrait )
    {
    }
}
