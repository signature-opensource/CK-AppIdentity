using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
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
    internal readonly bool _isLocalOrRemote;
    // The role of the application: set for a side, and for the common driver of a feature that has a side.
    bool? _isLocalSide;

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
    /// Initializes a new <see cref="ApplicationIdentityFeatureDriver"/> that is the "Local" or the "Remote" side of a feature
    /// that is a singleton within a domain: the type is "Local<paramref name="featureName"/>FeatureDriver" or
    /// "Remote<paramref name="featureName"/>FeatureDriver" and both sides manage the same <paramref name="featureName"/>.
    /// <para>
    /// The "AllowFeatures" and "DisallowFeatures" configurations name what a party is: "<paramref name="featureName"/>" on
    /// the party that is the feature (a local party of the Local side, a remote of the Remote side) and
    /// "<paramref name="featureName"/>Remote" on the parties that are its remotes (the remotes of the Local side, a local
    /// party of the Remote side). See <see cref="GetFeatureName(IParty)"/>.
    /// </para>
    /// <para>
    /// An application has one role: the "Local" and "Remote" drivers of a feature cannot be both registered. The drivers of a
    /// feature ("XXXFeatureDriver" and one of its sides) must agree on <paramref name="isAllowedByDefault"/>. The common driver
    /// "XXXFeatureDriver" has the role of the side: it reads the same names.
    /// </para>
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="featureName">The name of the feature: this type name without the "Local" or "Remote" prefix.</param>
    /// <param name="isAllowedByDefault">Whether the feature is opt-in or opt-out.</param>
    protected ApplicationIdentityFeatureDriver( ApplicationIdentityService s, string? featureName, bool isAllowedByDefault )
    {
        Throw.DebugAssert( "FeatureDriver".Length == 13 );
        var name = GetType().Name;
        if( name.EndsWith( "FeatureDriver_CK" ) ) name = name.Substring( 0, name.Length - 16 );
        else if( name.EndsWith( "FeatureDriver" ) ) name = name.Substring( 0, name.Length - 13 );
        else
        {
            Throw.InvalidOperationException( $"Invalid type name '{name}': a feature driver type name MUST be suffixed with 'FeatureDriver'." );
        }
        if( featureName != null )
        {
            if( featureName.Length == 0 || (name != "Local" + featureName && name != "Remote" + featureName) )
            {
                Throw.InvalidOperationException( $"Invalid feature name '{featureName}' for '{GetType().Name}': it must be 'Local{featureName}FeatureDriver' or 'Remote{featureName}FeatureDriver'." );
            }
            name = featureName;
        }
        bool isSide = featureName != null;
        if( isSide ) _isLocalSide = GetType().Name.StartsWith( "Local" );
        foreach( var other in s._builders )
        {
            if( other._featureName != name ) continue;
            if( other._isLocalOrRemote == isSide
                && (!isSide || other.GetType().Name.StartsWith( "Local" ) == GetType().Name.StartsWith( "Local" )) )
            {
                Throw.InvalidOperationException( $"'{GetType().ToCSharpName()}' and '{other.GetType().ToCSharpName()}' both manage the '{name}' feature." );
            }
            if( other._isLocalOrRemote && isSide )
            {
                Throw.InvalidOperationException( $"'{GetType().ToCSharpName()}' and '{other.GetType().ToCSharpName()}' cannot be both registered: an application is the Local or the Remote side of the '{name}' feature, not both." );
            }
            if( other._isAllowedByDefault != isAllowedByDefault )
            {
                Throw.InvalidOperationException( $"'{GetType().ToCSharpName()}' and '{other.GetType().ToCSharpName()}' disagree on whether the '{name}' feature is allowed by default." );
            }
            // The common driver and its side: the common driver has the role of the side (whichever is created first).
            if( isSide ) other._isLocalSide = _isLocalSide;
            else _isLocalSide = other._isLocalSide;
        }
        _isLocalOrRemote = isSide;
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
    /// The configured name is the one of the role of the party (see <see cref="GetFeatureName(IParty)"/>).
    /// </para>
    /// </summary>
    /// <param name="p">The party to test.</param>
    /// <returns>True if this feature is allowed, false otherwise.</returns>
    public bool IsAllowedFeature( IParty p ) => GetConfiguration( p ).IsAllowedFeature( GetFeatureName( p ), _isAllowedByDefault );

    /// <summary>
    /// Gets this feature name.
    /// This is this type name without the "FeatureDriver" suffix (and without the "Local" or "Remote" prefix of the side
    /// of a feature, see <see cref="ApplicationIdentityFeatureDriver(ApplicationIdentityService, string?, bool)"/>).
    /// </summary>
    public string FeatureName => _featureName;

    /// <summary>
    /// Gets whether the application is the Local side (true) or the Remote side (false) of this feature, or null when this
    /// feature has no side (or when only its common driver is registered).
    /// See <see cref="ApplicationIdentityFeatureDriver(ApplicationIdentityService, string?, bool)"/>.
    /// </summary>
    public bool? IsLocalSide => _isLocalSide;

    /// <summary>
    /// Gets the name that allows this feature on a party: the <see cref="FeatureName"/>, except for a feature with a side,
    /// where it is "<see cref="FeatureName"/>Remote" on a party that is a remote of the feature (a remote of the Local side,
    /// a local party of the Remote side).
    /// </summary>
    /// <param name="p">The party.</param>
    /// <returns>The feature name to use in the "AllowFeatures" and "DisallowFeatures" of the party.</returns>
    public string GetFeatureName( IParty p ) => IsRemoteOfTheFeature( p ) ? _featureName + "Remote" : _featureName;

    bool IsRemoteOfTheFeature( IParty p ) => _isLocalSide.HasValue && (p is ILocalParty) != _isLocalSide.Value;

    static ApplicationIdentityConfiguration GetConfiguration( IParty p ) => p is ILocalParty local ? local.LocalConfiguration : p.Configuration;

    // Called before the setup of a side: a party that allows only the name of the other role is an error (the configuration
    // of the other role, the remotes of a Remote side that allow "XXXRemote" for instance).
    internal bool CheckRoleNames( IActivityMonitor monitor, IEnumerable<IParty> parties )
    {
        Throw.DebugAssert( _isLocalOrRemote && _isLocalSide.HasValue );
        bool success = true;
        foreach( var p in parties )
        {
            var c = GetConfiguration( p );
            var name = GetFeatureName( p );
            var other = IsRemoteOfTheFeature( p ) ? _featureName : _featureName + "Remote";
            if( c.IsAllowedFeature( other, false ) && !c.IsAllowedFeature( name, _isAllowedByDefault ) )
            {
                var role = name == _featureName ? $"is the '{_featureName}'" : $"is a remote of the '{_featureName}'";
                monitor.Error( $"'{p}' allows the '{other}' feature but, in this application ('{GetType().Name}' is the {(_isLocalSide.Value ? "Local" : "Remote")} side of the '{_featureName}' feature), "
                               + $"it {role}: it must allow '{name}'." );
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
}
