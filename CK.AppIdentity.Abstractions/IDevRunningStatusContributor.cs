using CK.Core;
using System;
using System.Text.Json;

namespace CK.AppIdentity;

/// <summary>
/// A contribution of a package to the "DevRunningStatus.json" file that a party writes in its store folder when it runs in
/// the "#Dev" environment with a relative store (the "DevRunningStatus" feature): the launcher of a development solution
/// reads these files to know when the parties are ready and where they are.
/// <para>
/// <b>This code runs in production too, dormant:</b> a contributor subscribes to nothing and computes nothing before
/// <see cref="Activate"/> is called, which only an active writer does.
/// </para>
/// </summary>
[IsMultiple]
public interface IDevRunningStatusContributor : ISingletonAutoService
{
    /// <summary>
    /// Called once, by an active writer, before its first write. The contributor can subscribe to what changes its
    /// contribution and call <paramref name="onChanged"/> when it changes (the writer batches the rewrites).
    /// <para>
    /// This is called when the host has started (outside the agent's loop): the features are set up and the static
    /// features (the HTTP endpoints for instance) are started.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="onChanged">The callback to call when the contribution changes. It can be called from any thread.</param>
    void Activate( IActivityMonitor monitor, Action onChanged );

    /// <summary>
    /// Writes the properties of this contribution in the root object of the file (property names must not clash with the
    /// ones of the other contributors).
    /// <para>
    /// A contribution that is an object (a section, like "Transport") can hold a <c>"Pending"</c> array of strings: what an
    /// operator must do for the party (approve it on an approval page, for instance). The launcher prints them once the
    /// parties are running.
    /// </para>
    /// </summary>
    /// <param name="writer">The writer, positioned in the root object.</param>
    void Write( Utf8JsonWriter writer );

    /// <summary>
    /// Gets whether this contribution allows the party to be ready ("Ready" in the file): false while an outgoing remote
    /// is not connected, for instance. What waits for an operator is not a reason to be not ready: it is "Pending" (see
    /// <see cref="Write"/>).
    /// </summary>
    bool IsReady { get; }
}
