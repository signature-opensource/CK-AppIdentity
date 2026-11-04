using CK.Core;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Base class for the drivers of a static feature: a feature that is allowed only on the parties of the initial
/// configuration (the root, the tenant domains and the remotes that are not <see cref="IOwnedParty.IsDynamic"/>).
/// Its lifetime is the host's one: <see cref="OnHostStartedAsync"/> and <see cref="OnHostStoppingAsync"/> bracket it.
/// <para>
/// Only the parties that ARE the feature are constrained (see <see cref="ApplicationIdentityFeatureDriver.GetFeatureName(IParty)"/>):
/// the remotes that use an asymmetric feature can be dynamic (a front added at run time is a remote of an AuthCenter). Dynamic
/// parties (and dynamic tenant domains) are still supported: they just cannot have a static feature. A dynamic party that
/// explicitly allows it fails its setup; when the feature is allowed by default, the default doesn't apply to them (see
/// <see cref="ApplicationIdentityFeatureDriver.IsAllowedFeature(IParty)"/>).
/// </para>
/// </summary>
[CKTypeDefiner]
public abstract class ApplicationIdentityStaticFeatureDriver : ApplicationIdentityFeatureDriver
{
    /// <summary>
    /// Initializes a new <see cref="ApplicationIdentityStaticFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="isAllowedByDefault">Whether the feature is opt-in or opt-out.</param>
    protected ApplicationIdentityStaticFeatureDriver( ApplicationIdentityService s, bool isAllowedByDefault )
        : this( s, null, isAllowedByDefault )
    {
    }

    /// <summary>
    /// Initializes a new <see cref="ApplicationIdentityStaticFeatureDriver"/> of an asymmetric feature.
    /// See <see cref="ApplicationIdentityFeatureDriver(ApplicationIdentityService, string?, bool)"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="featureName">The name of an asymmetric feature (null for a symmetric one).</param>
    /// <param name="isAllowedByDefault">
    /// Whether the feature is opt-in or opt-out. For an asymmetric feature, true makes little sense and must be exceptional.
    /// </param>
    protected ApplicationIdentityStaticFeatureDriver( ApplicationIdentityService s, string? featureName, bool isAllowedByDefault = false )
        : base( s, featureName, isAllowedByDefault )
    {
        _isStatic = true;
    }

    /// <summary>
    /// Sealed: a dynamic party that is this feature and explicitly allows it is an error. The dynamic remotes of the
    /// feature are handled by <see cref="SetupDynamicFeatureRemoteAsync(FeatureLifetimeContext, IOwnedParty)"/>.
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <param name="party">The dynamic party to initialize.</param>
    /// <returns>True on success, false on error.</returns>
    protected internal sealed override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        bool success = true;
        foreach( var p in context.GetAllLocals().Concat<IParty>( context.GetAllRemotes() ) )
        {
            if( IsDynamicFeatureParty( p ) && IsExplicitlyAllowed( p ) )
            {
                context.Monitor.Error( $"'{p}' is a dynamic party: it cannot allow '{GetFeatureName( p )}'. The '{FeatureName}' feature is static, " +
                                       $"it can only be allowed on the parties of the initial configuration." );
                success = false;
            }
        }
        return success ? SetupDynamicFeatureRemoteAsync( context, party ) : Task.FromResult( false );
    }

    /// <summary>
    /// Sealed: calls <see cref="TeardownDynamicFeatureRemoteAsync(FeatureLifetimeContext, IOwnedParty)"/>.
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <param name="party">The dynamic party to cleanup.</param>
    /// <returns>The awaitable.</returns>
    protected internal sealed override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        return TeardownDynamicFeatureRemoteAsync( context, party );
    }

    /// <summary>
    /// Called for a dynamic party (see <see cref="ApplicationIdentityFeatureDriver.SetupDynamicRemoteAsync(FeatureLifetimeContext, IOwnedParty)"/>)
    /// once checked that no dynamic party of the operation is this feature: only the remotes of the feature (when it has
    /// a side) may concern this driver.
    /// <para>
    /// Does nothing by default.
    /// </para>
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <param name="party">The dynamic party to initialize.</param>
    /// <returns>True on success, false on non recoverable error (errors must be logged).</returns>
    protected virtual Task<bool> SetupDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

    /// <summary>
    /// Called for a dynamic party (see <see cref="ApplicationIdentityFeatureDriver.TeardownDynamicRemoteAsync(FeatureLifetimeContext, IOwnedParty)"/>).
    /// <para>
    /// Does nothing by default.
    /// </para>
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <param name="party">The dynamic party to cleanup.</param>
    /// <returns>The awaitable.</returns>
    protected virtual Task TeardownDynamicFeatureRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

    /// <summary>
    /// Called once the initialization has succeeded (all the features are set up and published), outside the agent's
    /// loop, in the order of the drivers: this is where a driver opens what makes the party reachable from the outside
    /// (an HTTP endpoint for instance).
    /// <para>
    /// This is called by the hosted service's <c>StartedAsync</c> or by <see cref="ApplicationIdentityService.StartAndInitializeAsync"/>.
    /// A failure fails the start: the drivers already started are stopped (<see cref="OnHostStoppingAsync"/>).
    /// </para>
    /// <para>
    /// Does nothing by default.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="cancel">The start cancellation token.</param>
    /// <returns>The awaitable.</returns>
    protected internal virtual Task OnHostStartedAsync( IActivityMonitor monitor, CancellationToken cancel ) => Task.CompletedTask;

    /// <summary>
    /// Called before the agent stops (before any teardown), outside its loop, in the reverse order of the drivers, and
    /// only if <see cref="OnHostStartedAsync"/> succeeded. This is where a driver closes what it opened: the parties and
    /// their features are still there (the requests in progress can complete).
    /// <para>
    /// This is called by the hosted service's <c>StoppingAsync</c> or by <see cref="ApplicationIdentityService.DisposeAsync"/>.
    /// Exceptions are logged.
    /// </para>
    /// <para>
    /// Does nothing by default.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="cancel">The stop cancellation token.</param>
    /// <returns>The awaitable.</returns>
    protected internal virtual Task OnHostStoppingAsync( IActivityMonitor monitor, CancellationToken cancel ) => Task.CompletedTask;
}
