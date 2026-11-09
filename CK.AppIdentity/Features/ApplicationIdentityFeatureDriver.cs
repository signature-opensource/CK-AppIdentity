using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// Base class for feature builders. Such builders are singleton auto services that can depend on
/// any other singleton services, including other <see cref="ApplicationIdentityFeatureDriver"/>. 
/// </summary>
[CKTypeDefiner]
public abstract class ApplicationIdentityFeatureDriver : IApplicationIdentityFeatureDriver
{
    readonly ApplicationIdentityService _s;
    readonly string _featureName;
    readonly bool _isAllowedByDefault;
    // The drivers of an asymmetric feature share this (null for a symmetric feature).
    internal readonly AsymmetricDrivers? _asymmetric;
    // Set by ApplicationIdentityStaticFeatureDriver.
    internal bool _isStatic;

    internal sealed class AsymmetricDrivers
    {
        public ApplicationIdentityFeatureDriver? Common;
        public ApplicationIdentityFeatureDriver? Local;
        public ApplicationIdentityFeatureDriver? Remote;
        // The first registered driver checks the names (once per feature).
        public ApplicationIdentityFeatureDriver? First;
    }

    /// <summary>
    /// Initializes a new <see cref="ApplicationIdentityFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="isAllowedByDefault">Whether the feature is opt-in or opt-out.</param>
    protected ApplicationIdentityFeatureDriver( ApplicationIdentityService s, bool isAllowedByDefault )
        : this( s, null, isAllowedByDefault )
    {
    }

    /// <summary>
    /// Initializes a new <see cref="ApplicationIdentityFeatureDriver"/> of an <b>asymmetric feature</b> when
    /// <paramref name="featureName"/> is not null: a local party implements the feature, and the parties that use it
    /// relay to it. The type is "<paramref name="featureName"/>FeatureDriver" (the optional common driver),
    /// "Local<paramref name="featureName"/>FeatureDriver" (the Local side: a local party is the feature) or
    /// "Remote<paramref name="featureName"/>FeatureDriver" (the Remote side: a local party relays to the remote that is
    /// the feature). They manage the same <paramref name="featureName"/>.
    /// <para>
    /// The "AllowFeatures" and "DisallowFeatures" configurations use "<paramref name="featureName"/>" on a local party (in its
    /// "Local") and "<paramref name="featureName"/>Remote" on a remote (see <see cref="GetFeatureName(IParty)"/>):
    /// <list type="bullet">
    ///   <item>
    ///   A local party that allows "<paramref name="featureName"/>" <b>is</b> the feature: its remotes that allow
    ///   "<paramref name="featureName"/>Remote" are the ones that can use it (the Local side).
    ///   </item>
    ///   <item>
    ///   A local party that doesn't allow it uses <b>the</b> remote that allows "<paramref name="featureName"/>Remote", if
    ///   any: this remote is the feature (the Remote side, see <see cref="TryGetFeatureRemote(IActivityMonitor, ILocalParty, out IRemoteParty?)"/>).
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// The Local and the Remote drivers can be both registered: the local "<paramref name="featureName"/>" gives the role of
    /// each local party. The drivers of a feature must agree on <paramref name="isAllowedByDefault"/>.
    /// </para>
    /// <para>
    /// An asymmetric feature allowed by default makes little sense: every local party would be the feature and every
    /// remote one of its callers, unless the configuration disallows it. This must be exceptional: the default is false.
    /// </para>
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="featureName">
    /// The name of an asymmetric feature: this type name without the "Local" or "Remote" prefix. Null for a symmetric feature
    /// (its name is the type name).
    /// </param>
    /// <param name="isAllowedByDefault">
    /// Whether the feature is opt-in or opt-out. For an asymmetric feature, true makes little sense and must be exceptional.
    /// </param>
    protected ApplicationIdentityFeatureDriver( ApplicationIdentityService s, string? featureName, bool isAllowedByDefault = false )
    {
        Throw.DebugAssert( "FeatureDriver".Length == 13 );
        var name = GetType().Name;
        if( name.EndsWith( "FeatureDriver_CK" ) ) name = name.Substring( 0, name.Length - 16 );
        else if( name.EndsWith( "FeatureDriver" ) ) name = name.Substring( 0, name.Length - 13 );
        else
        {
            Throw.InvalidOperationException( $"Invalid type name '{name}': a feature driver type name MUST be suffixed with 'FeatureDriver'." );
        }
        // 0: symmetric, 1: common, 2: Local, 3: Remote.
        int kind = 0;
        if( featureName != null )
        {
            if( featureName.Length > 0 && name == featureName ) kind = 1;
            else if( featureName.Length > 0 && name == "Local" + featureName ) kind = 2;
            else if( featureName.Length > 0 && name == "Remote" + featureName ) kind = 3;
            else
            {
                Throw.InvalidOperationException( $"Invalid feature name '{featureName}' for '{GetType().Name}': it must be '{featureName}FeatureDriver', 'Local{featureName}FeatureDriver' or 'Remote{featureName}FeatureDriver'." );
            }
            name = featureName;
        }
        AsymmetricDrivers? asymmetric = kind > 0 ? new AsymmetricDrivers() : null;
        foreach( var other in s._builders )
        {
            if( other._featureName != name ) continue;
            if( (other._asymmetric == null) != (asymmetric == null) )
            {
                var symmetric = asymmetric == null ? this : other;
                Throw.InvalidOperationException( $"'{GetType().ToCSharpName()}' and '{other.GetType().ToCSharpName()}' manage the '{name}' feature but '{symmetric.GetType().ToCSharpName()}' doesn't give the feature name "
                                                 + $"to the base constructor: the drivers of an asymmetric feature must give it." );
            }
            if( asymmetric == null
                || (kind == 1 && other._asymmetric!.Common != null)
                || (kind == 2 && other._asymmetric!.Local != null)
                || (kind == 3 && other._asymmetric!.Remote != null) )
            {
                Throw.InvalidOperationException( $"'{GetType().ToCSharpName()}' and '{other.GetType().ToCSharpName()}' both manage the '{name}' feature." );
            }
            if( other._isAllowedByDefault != isAllowedByDefault )
            {
                Throw.InvalidOperationException( $"'{GetType().ToCSharpName()}' and '{other.GetType().ToCSharpName()}' disagree on whether the '{name}' feature is allowed by default." );
            }
            asymmetric = other._asymmetric;
        }
        if( asymmetric != null )
        {
            asymmetric.First ??= this;
            if( kind == 1 ) asymmetric.Common = this;
            else if( kind == 2 ) asymmetric.Local = this;
            else asymmetric.Remote = this;
        }
        _asymmetric = asymmetric;
        _isAllowedByDefault = isAllowedByDefault;
        _featureName = name;
        // Adding the builder to the list here captures the topological
        // dependency order of the feature builders.
        s._builders.Add( this );
        _s = s;
    }

    /// <summary>
    /// Gets the application identity service.
    /// </summary>
    public ApplicationIdentityService ApplicationIdentityService => _s;

    /// <summary>
    /// Gets whether this feature is allowed or disabled for the root <see cref="ApplicationIdentityService"/> (its "Local"
    /// configuration). Use <see cref="IsAllowedFeature(IParty)"/> for any other party.
    /// </summary>
    public bool IsRootAllowed => IsAllowedFeature( _s );

    /// <summary>
    /// Gets whether this feature is enabled for the given party (including the root <see cref="IApplicationIdentityService"/>):
    /// a local party (the root or a tenant domain) answers from its <see cref="ILocalParty.LocalConfiguration"/>, a remote from
    /// its own configuration. Both resolve the Allow/DisallowFeatures of the "Defaults" sections above them.
    /// <para>
    /// On a remote, the configured name of an asymmetric feature is "<see cref="FeatureName"/>Remote" (see <see cref="GetFeatureName(IParty)"/>).
    /// </para>
    /// <para>
    /// A static feature (<see cref="ApplicationIdentityStaticFeatureDriver"/>) is never allowed on a dynamic party that
    /// is the feature.
    /// </para>
    /// </summary>
    /// <param name="p">The party to test.</param>
    /// <returns>True if this feature is allowed, false otherwise.</returns>
    public bool IsAllowedFeature( IParty p )
    {
        if( _isStatic && IsDynamicFeatureParty( p ) ) return false;
        return GetConfiguration( p ).IsAllowedFeature( GetFeatureName( p ), _isAllowedByDefault );
    }

    // A dynamic party that is the feature: a static feature cannot be allowed on it. For an asymmetric feature, a remote
    // is the feature when its local party is not (otherwise it uses the feature of its local party).
    internal bool IsDynamicFeatureParty( IParty p )
    {
        if( p is not IOwnedParty { IsDynamic: true } ) return false;
        return _asymmetric == null
               || p is not IRemoteParty r
               || !GetConfiguration( r.Owner ).IsAllowedFeature( _featureName, _isAllowedByDefault );
    }

    // Whether the configuration of the party explicitly allows this feature (the default doesn't apply).
    internal bool IsExplicitlyAllowed( IParty p ) => GetConfiguration( p ).IsAllowedFeature( GetFeatureName( p ), false );

    /// <summary>
    /// Gets this feature name.
    /// This is this type name without the "FeatureDriver" suffix (and without the "Local" or "Remote" prefix of the side
    /// of an asymmetric feature, see <see cref="ApplicationIdentityFeatureDriver(ApplicationIdentityService, string?, bool)"/>).
    /// </summary>
    public string FeatureName => _featureName;

    /// <summary>
    /// Gets whether this feature is asymmetric. See <see cref="ApplicationIdentityFeatureDriver(ApplicationIdentityService, string?, bool)"/>.
    /// </summary>
    public bool IsAsymmetric => _asymmetric != null;

    /// <summary>
    /// Gets whether the Local side of this asymmetric feature ("Local<see cref="FeatureName"/>FeatureDriver") is registered:
    /// a local party can be the feature.
    /// </summary>
    public bool HasLocalSide => _asymmetric?.Local != null;

    /// <summary>
    /// Gets whether the Remote side of this asymmetric feature ("Remote<see cref="FeatureName"/>FeatureDriver") is registered:
    /// a local party can use a remote that is the feature.
    /// </summary>
    public bool HasRemoteSide => _asymmetric?.Remote != null;

    /// <summary>
    /// Gets the name that allows this feature on a party: the <see cref="FeatureName"/>, except on a remote for an asymmetric
    /// feature, where it is "<see cref="FeatureName"/>Remote".
    /// </summary>
    /// <param name="p">The party.</param>
    /// <returns>The feature name to use in the "AllowFeatures" and "DisallowFeatures" of the party.</returns>
    public string GetFeatureName( IParty p ) => _asymmetric != null && p is not ILocalParty ? _featureName + "Remote" : _featureName;

    /// <summary>
    /// Gets the remote that is this asymmetric feature for a local party: when the local party doesn't allow the
    /// <see cref="FeatureName"/>, this is the remote that allows "<see cref="FeatureName"/>Remote". There must be at most one:
    /// more than one is an error (a "Defaults" that allows it applies to every remote below it).
    /// <para>
    /// <paramref name="featureRemote"/> is null when the local party is the feature or when none of its remotes is.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor that logs the error.</param>
    /// <param name="local">The local party.</param>
    /// <param name="featureRemote">The remote that is the feature.</param>
    /// <returns>False on error.</returns>
    public bool TryGetFeatureRemote( IActivityMonitor monitor, ILocalParty local, out IRemoteParty? featureRemote )
    {
        Throw.CheckState( IsAsymmetric );
        featureRemote = null;
        if( IsAllowedFeature( local ) ) return true;
        IRemoteParty? found = null;
        foreach( var r in local.Remotes )
        {
            if( !IsAllowedFeature( r ) ) continue;
            if( found != null )
            {
                var all = local.Remotes.Where( IsAllowedFeature ).Select( x => x.ToString() ).Concatenate( "', '" );
                monitor.Error( $"'{local}' doesn't allow the '{_featureName}' feature: it can use only one remote that is the '{_featureName}' but the remotes '{all}' "
                               + $"allow '{_featureName}Remote' (a \"Defaults\" that allows it applies to every remote below it)." );
                return false;
            }
            found = r;
        }
        featureRemote = found;
        return true;
    }

    static ApplicationIdentityConfiguration GetConfiguration( IParty p ) => p is ILocalParty local ? local.LocalConfiguration : p.Configuration;

    // Called before the setup of the first registered driver of an asymmetric feature: a local party that allows only
    // "XXXRemote" or a remote that allows only "XXX" is an error.
    internal bool CheckAsymmetricNames( IActivityMonitor monitor, IEnumerable<IParty> parties )
    {
        Throw.DebugAssert( _asymmetric != null );
        bool success = true;
        foreach( var p in parties )
        {
            var c = GetConfiguration( p );
            var name = GetFeatureName( p );
            var other = p is ILocalParty ? _featureName + "Remote" : _featureName;
            if( c.IsAllowedFeature( other, false ) && !c.IsAllowedFeature( name, _isAllowedByDefault ) )
            {
                if( p is ILocalParty )
                {
                    monitor.Error( $"'{p}' allows the '{other}' feature in its \"Local\": '{other}' is allowed on a remote. The '{_featureName}' feature is asymmetric: "
                                   + $"a local party that is the '{_featureName}' allows '{_featureName}', a local party that uses a remote '{_featureName}' allows nothing." );
                }
                else
                {
                    monitor.Error( $"'{p}' allows the '{other}' feature: '{other}' is allowed in the \"Local\" of a local party. The '{_featureName}' feature is asymmetric: "
                                   + $"a remote that is the '{_featureName}' (or that uses the local '{_featureName}') allows '{name}'." );
                }
                success = false;
            }
        }
        return success;
    }

    /// <summary>
    /// Must do whatever is required to register features into any <see cref="IParty.Features"/>
    /// of <see cref="IApplicationIdentityService.AllParties"/>.
    /// <para>
    /// The 2 helpers <see cref="FeatureLifetimeContext.GetAllRemotes()"/> and <see cref="FeatureLifetimeContext.GetAllLocals()"/> can be useful.
    /// </para>
    /// <para>
    /// The <see cref="ApplicationIdentityService"/> property is available as well as helpers to know if this feature is allowed on
    /// a party (see <see cref="IsAllowedFeature(IParty)"/>).
    /// </para>
    /// <para>
    /// This is called in the same order as this driver has been instantiated: any dependent feature drivers have been initialized.
    /// </para>
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <returns>True on success, false on non recoverable error (errors must be logged).</returns>
    internal protected abstract Task<bool> SetupAsync( FeatureLifetimeContext context );

    /// <summary>
    /// Must do whatever is required to register features into <see cref="ApplicationIdentityParty.Features"/> for the <paramref name="party"/>
    /// and any <see cref="ILocalParty.Remotes"/> if the party is a <see cref="TenantDomainParty"/>.
    /// <para>
    /// The 2 helpers <see cref="FeatureLifetimeContext.GetAllRemotes()"/> and <see cref="FeatureLifetimeContext.GetAllLocals()"/> can be useful.
    /// </para>
    /// <para>
    /// The <see cref="ApplicationIdentityService"/> property is available as well as helpers to know if this feature is allowed on
    /// a party (see <see cref="IsAllowedFeature(IParty)"/>).
    /// </para>
    /// <para>
    /// This is called in the same order as this driver has been instantiated: any dependent feature drivers have been initialized.
    /// </para>
    /// <para>
    /// The <paramref name="party"/> being set up is not yet in the <see cref="ILocalParty.Remotes"/> of its local party: a driver
    /// that looks for remotes there doesn't see it and must handle this party directly.
    /// </para>
    /// <para>
    /// The setup of a party fails if any driver returns false or throws. All the drivers are called anyway: when it fails,
    /// <see cref="TeardownDynamicRemoteAsync(FeatureLifetimeContext, IOwnedParty)"/> is NOT called for this party. A driver that
    /// must undo its work registers a <see cref="BasicTrampoline.OnError(System.Action)"/> handler on the <see cref="FeatureLifetimeContext.Trampoline"/>
    /// when it does it.
    /// </para>
    /// <para>
    /// Dynamic parties are added all or nothing: when another party of the same batch fails, the parties that have been successfully
    /// set up are torn down (they have never been published).
    /// </para>
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <param name="party">The dynamic party to initialize.</param>
    /// <returns>True on success, false on non recoverable error (errors must be logged).</returns>
    internal protected abstract Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party );

    /// <summary>
    /// Called when a dynamic party is destroyed, or when it has been successfully set up but another party of the same batch
    /// failed (the party has never been published).
    /// <para>
    /// This is called only for parties that have been successfully set up by all the drivers.
    /// </para>
    /// <para>
    /// This is called in reverse order (from most dependent feature drivers to basic ones).
    /// </para>
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <param name="party">The dynamic party to cleanup.</param>
    /// <returns>The awaitable.</returns>
    internal protected abstract Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party );

    /// <summary>
    /// Called by a stopping agent. Must get rid of any acquired resources at any level.
    /// <para>
    /// This is called in reverse order (from most dependent feature drivers to basic ones).
    /// </para>
    /// <para>
    /// This is called even if <see cref="SetupAsync(FeatureLifetimeContext)"/> failed for this driver or for another one:
    /// it must handle a partial setup.
    /// </para>
    /// </summary>
    /// <param name="context">The lifetime context.</param>
    /// <returns>The awaitable.</returns>
    internal protected abstract Task TeardownAsync( FeatureLifetimeContext context );

    /// <summary>
    /// Contributes to the <see cref="IParty.Description"/> of a local party (the root or a tenant domain): what this
    /// feature states about the party (its client applications, its public URL...). Does nothing by default.
    /// <para>
    /// This is called once all the drivers have set up the party, in the order of the drivers, and again each time a driver
    /// signals that its contribution changed (see <see cref="OnDescriptionChangedAsync(IActivityMonitor, ILocalParty?)"/>).
    /// The <paramref name="builder"/> starts with the configured "Local:PublicUrl", when there is one: a contribution
    /// doesn't override it.
    /// </para>
    /// </summary>
    /// <param name="party">The local party to describe.</param>
    /// <param name="builder">The description builder.</param>
    protected internal virtual void ContributeDescription( ILocalParty party, PartyDescription.Builder builder )
    {
    }

    /// <summary>
    /// The second phase of the description of a local party: derives values from what the first phase states (see
    /// <see cref="ContributeDescription(ILocalParty, PartyDescription.Builder)"/>), the routes for instance. Does nothing
    /// by default.
    /// <para>
    /// This is called for every driver after the first phase of every driver, in the order of the drivers. A derived value
    /// derives from the first phase only.
    /// </para>
    /// </summary>
    /// <param name="party">The local party to describe.</param>
    /// <param name="builder">The second phase of the description.</param>
    protected internal virtual void ContributeDerivedDescription( ILocalParty party, PartyDescription.DerivedBuilder builder )
    {
    }

    /// <summary>
    /// Signals that the contribution of this driver to the description of a local party changed (see
    /// <see cref="ContributeDescription(ILocalParty, PartyDescription.Builder)"/>): the description is computed again and
    /// <see cref="ILocalParty.DescriptionChanged"/> is raised when it changed. Before the descriptions are computed for
    /// the first time (during the setup), this does nothing.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="party">The local party to describe again. Null for all the local parties.</param>
    /// <returns>The awaitable.</returns>
    protected Task OnDescriptionChangedAsync( IActivityMonitor monitor, ILocalParty? party = null )
    {
        return _s._descriptionDriver?.RefreshAsync( monitor, party ) ?? Task.CompletedTask;
    }
}
