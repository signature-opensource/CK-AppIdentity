using CK.Core;
using CK.Setup;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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
    readonly TypeActivation _activation;
    // The party component of this driver (a generated specialization is described by its base type): the owner type of its options.
    readonly System.Type _componentType;
    // The options of each party (created before the setup of the party).
    readonly ConditionalWeakTable<IParty, IReadOnlyList<object>> _options;
    readonly ConditionalWeakTable<IParty, object> _models;
    readonly ConfigurationModelDescription? _model;
    // The drivers of an asymmetric feature share this (null for a symmetric feature).
    internal readonly AsymmetricDrivers? _asymmetric;
    // Set by ApplicationIdentityStaticFeatureDriver.
    internal bool _isStatic;

    internal sealed class AsymmetricDrivers
    {
        public ApplicationIdentityFeatureDriver? Local;
        public ApplicationIdentityFeatureDriver? Remote;
        // The first registered driver checks the names (once per feature).
        public ApplicationIdentityFeatureDriver? First;
    }

    /// <summary>
    /// Initializes a new <see cref="ApplicationIdentityFeatureDriver"/>. Its feature name, its activation and the
    /// side of an asymmetric feature are declared by its <see cref="PartyFeatureAttribute"/> and read in the
    /// <see cref="ApplicationIdentityService.ComponentDirectory"/> (the engine computes them).
    /// <para>
    /// An <b>asymmetric feature</b> has two drivers: a local party implements the feature, and the parties that use it
    /// relay to it. A local party declares "XXX" (in its "Local") and a remote declares "XXXRemote" (see
    /// <see cref="GetFeatureName(IParty)"/>):
    /// <list type="bullet">
    ///   <item>
    ///   A local party that declares "XXX" <b>is</b> the feature: its remotes that declare "XXXRemote" are the ones that can
    ///   use it (the Local side).
    ///   </item>
    ///   <item>
    ///   A local party that doesn't declare it uses <b>the</b> remote that declares "XXXRemote", if any: this remote is the
    ///   feature (the Remote side, see <see cref="TryGetFeatureRemote(IActivityMonitor, ILocalParty, out IRemoteParty?)"/>).
    ///   </item>
    /// </list>
    /// The Local side requires the Remote side: the "XXXRemote" key of a remote is read by both sides. A common
    /// implementation of the two sides is first a shared static helper; a common abstract base class is possible.
    /// </para>
    /// </summary>
    /// <param name="s">The application identity service.</param>
    protected ApplicationIdentityFeatureDriver( ApplicationIdentityService s )
    {
        Throw.CheckNotNullArgument( s );
        // A generated specialization ("XXXFeatureDriver_CK") is described by its base type.
        var type = GetType();
        var info = s.ComponentDirectory.Find( type ) ?? (type.BaseType != null ? s.ComponentDirectory.Find( type.BaseType ) : null);
        Throw.CheckState( $"The feature driver '{type.ToCSharpName()}' is not a party component of the application.", info != null && info.FeatureName != null );
        var name = info.FeatureName;
        AsymmetricDrivers? asymmetric = null;
        if( info.Side != FeatureSide.None )
        {
            foreach( var other in s._builders )
            {
                if( other._asymmetric != null && string.Equals( other._featureName, name, System.StringComparison.OrdinalIgnoreCase ) )
                {
                    asymmetric = other._asymmetric;
                    break;
                }
            }
            asymmetric ??= new AsymmetricDrivers();
            asymmetric.First ??= this;
            if( info.Side == FeatureSide.Local ) asymmetric.Local = this;
            else asymmetric.Remote = this;
        }
        _asymmetric = asymmetric;
        _componentType = info.ComponentType;
        _options = new ConditionalWeakTable<IParty, IReadOnlyList<object>>();
        _models = new ConditionalWeakTable<IParty, object>();
        _model = info.Model;
        _isAllowedByDefault = !info.IsOptional;
        _activation = info.Activation;
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
    /// its own configuration. Both look up the declaration of the feature (its key) in their own keys, then in the "Defaults"
    /// sections above them.
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
        if( !IsMember( p ) ) return false;
        return IsAllowed( GetConfiguration( p ), GetFeatureName( p ) );
    }

    /// <summary>
    /// Gets whether this feature is a member of the type set of a party (the set of its local party for a remote): the
    /// types drive, a feature that is not a member doesn't apply to the party, whatever its configuration says.
    /// </summary>
    /// <param name="p">The party.</param>
    /// <returns>True when this feature is a member of the party's set (or when the party has no known set).</returns>
    public bool IsMember( IParty p ) => _s.ComponentDirectory.IsMember( TypeSetName( p ), _componentType );

    // The party component that this driver is (a generated specialization is described by its base type).
    internal System.Type ComponentType => _componentType;

    // The type set of a local party is named after its party name; a remote uses the one of its local party. A tenant domain
    // of a template uses the set of its template ("${Name}"): never a set found by its own name, that could match the pattern
    // of another template of the application (a tenant "$Worker-9" next to a fleet "$Worker-{Name}").
    internal static string TypeSetName( IParty p )
    {
        var local = p as ILocalParty ?? (p as IOwnedParty)?.Owner;
        if( local is ITenantDomainParty { Configuration.Template: { } template } ) return template.PartyNamePattern;
        return (local ?? p).PartyName;
    }

    // A Required feature is always allowed ("false" is a start error, see AppIdentityAgent.CheckFeatureKeys).
    bool IsAllowed( ApplicationIdentityConfiguration c, string name ) => _activation == TypeActivation.Required
                                                                         || c.IsAllowedFeature( name, _isAllowedByDefault );

    /// <summary>
    /// Gets the activation of this feature (declared by its <see cref="PartyFeatureAttribute"/>).
    /// </summary>
    public TypeActivation Activation => _activation;

    // A dynamic party that is the feature: a static feature cannot be allowed on it. For an asymmetric feature, a remote
    // is the feature when its local party is not (otherwise it uses the feature of its local party).
    internal bool IsDynamicFeatureParty( IParty p )
    {
        if( p is not IOwnedParty { IsDynamic: true } ) return false;
        return _asymmetric == null
               || p is not IRemoteParty r
               || !IsAllowed( GetConfiguration( r.Owner ), _featureName );
    }

    // Whether the configuration of the party explicitly allows this feature (the default doesn't apply).
    internal bool IsExplicitlyAllowed( IParty p ) => GetConfiguration( p ).IsAllowedFeature( GetFeatureName( p ), false );

    /// <summary>
    /// Gets this feature name.
    /// This is the name declared by its <see cref="PartyFeatureAttribute"/> (the same for the two sides of an asymmetric
    /// feature).
    /// </summary>
    public string FeatureName => _featureName;

    /// <summary>
    /// Gets whether this feature is asymmetric. See <see cref="ApplicationIdentityFeatureDriver(ApplicationIdentityService)"/>.
    /// </summary>
    public bool IsAsymmetric => _asymmetric != null;

    /// <summary>
    /// Gets whether the Local side of this asymmetric feature is registered:
    /// a local party can be the feature.
    /// </summary>
    public bool HasLocalSide => _asymmetric?.Local != null;

    /// <summary>
    /// Gets whether the Remote side of this asymmetric feature is registered:
    /// a local party can use a remote that is the feature.
    /// </summary>
    public bool HasRemoteSide => _asymmetric?.Remote != null;

    /// <summary>
    /// Gets the key that declares this feature on a party: the <see cref="FeatureName"/>, except on a remote for an asymmetric
    /// feature, where it is "<see cref="FeatureName"/>Remote".
    /// </summary>
    /// <param name="p">The party.</param>
    /// <returns>The key that declares this feature on the party.</returns>
    public string GetFeatureName( IParty p ) => _asymmetric != null && p is not ILocalParty ? _featureName + "Remote" : _featureName;

    /// <summary>
    /// Gets the remote that is this asymmetric feature for a local party: when the local party doesn't declare the
    /// <see cref="FeatureName"/>, this is the remote that declares "<see cref="FeatureName"/>Remote". There must be at most one:
    /// more than one is an error (a "Defaults" that declares it applies to every remote below it).
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
                monitor.Error( $"'{local}' doesn't declare the '{_featureName}' feature: it can use only one remote that is the '{_featureName}' but the remotes '{all}' "
                               + $"declare '{_featureName}Remote' (a \"Defaults\" that declares it applies to every remote below it)." );
                return false;
            }
            found = r;
        }
        featureRemote = found;
        return true;
    }

    static ApplicationIdentityConfiguration GetConfiguration( IParty p ) => p is ILocalParty local ? local.LocalConfiguration : p.Configuration;

    /// <summary>
    /// Gets the options of this feature for a party, in their order: the options of this driver
    /// (<see cref="IPartyComponentOption{TOwner}"/>, <see cref="PartyFeatureOptionAttribute"/>) that the feature's section
    /// enables. They are created before this driver sets the party up (an option reads its configuration in its
    /// constructor). Empty when the feature is not allowed on the party.
    /// </summary>
    /// <param name="party">The party.</param>
    /// <returns>The options.</returns>
    public IReadOnlyList<object> GetOptions( IParty party )
    {
        Throw.CheckNotNullArgument( party );
        return _options.TryGetValue( party, out var o ) ? o : [];
    }

    /// <summary>
    /// Gets the configuration model of this feature for a party: its <c>[Configuration&lt;TModel&gt;]</c>, read from the
    /// feature's section of the party key by key along its levels (the party, then the "Defaults" above it), the names of
    /// its options reserved. It is read before this driver sets the party up (with its options): an invalid section fails
    /// the setup (every error is logged), a party without section gets the defaults of the model.
    /// <para>
    /// The model of an asymmetric feature is the one of its side: the Local side has the model of the local parties that
    /// declare "XXX", the Remote side the model of the remotes that declare "XXXRemote".
    /// </para>
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <param name="party">A party on which this feature is allowed.</param>
    /// <returns>The model.</returns>
    public TModel GetModel<TModel>( IParty party ) where TModel : class
    {
        Throw.CheckNotNullArgument( party );
        if( _model == null ) Throw.InvalidOperationException( $"The feature '{_featureName}' has no configuration model (its driver has no [Configuration<TModel>])." );
        if( !_models.TryGetValue( party, out var m ) ) Throw.InvalidOperationException( $"The feature '{_featureName}' is not allowed on '{party}' (or the party is not set up yet): it has no model." );
        return (TModel)m;
    }

    /// <summary>
    /// Gets an option of this feature for a party (see <see cref="GetOptions(IParty)"/>).
    /// </summary>
    /// <typeparam name="T">The option type.</typeparam>
    /// <param name="party">The party.</param>
    /// <returns>The option, null when it is not enabled.</returns>
    public T? GetOption<T>( IParty party ) where T : class => GetOptions( party ).OfType<T>().FirstOrDefault();

    // The model of an asymmetric feature is the one of its side: the Local side reads the "XXX" section of the local
    // parties, the Remote side the "XXXRemote" section of the remotes.
    bool IsModelSide( IParty p ) => _asymmetric == null || (p is ILocalParty) == (_asymmetric.Local == this);

    // Reads the model and creates the options of the parties on which this feature is allowed (once per party), before
    // the driver's setup. The option of a feature is looked up in the party's own keys, then in the "Defaults" above it
    // (the nearest wins; a configuration model is read key by key along them).
    internal bool CreateOptions( IActivityMonitor monitor, IEnumerable<IParty> parties )
    {
        var directory = _s.OptionDirectory;
        var options = directory?.GetOptions( _componentType ) ?? [];
        bool success = true;
        if( _model != null )
        {
            foreach( var p in parties )
            {
                if( _models.TryGetValue( p, out _ ) || !IsAllowedFeature( p ) || !IsModelSide( p ) ) continue;
                var c = GetConfiguration( p );
                var key = GetFeatureName( p );
                var collector = new UserMessageCollector( new CurrentCultureInfo( new TranslationService(), NormalizedCultureInfo.CodeDefault ) );
                var model = ConfigurationModelReader.TryRead( _model, c.GetFeatureLevels( key ), $"{c.Configuration.Path}:{key}", collector, options.Select( o => o.Name ) );
                collector.DumpLogs( monitor );
                if( model == null ) success = false;
                else _models.Add( p, model );
            }
        }
        if( options.Count == 0 ) return success;
        foreach( var p in parties )
        {
            if( _options.TryGetValue( p, out _ ) || !IsAllowedFeature( p ) ) continue;
            var c = GetConfiguration( p );
            var key = GetFeatureName( p );
            if( PartyComponentOptionDirectory.TryCreate( monitor,
                                                         options,
                                                         name => c.GetFeatureSections( key, name ),
                                                         $"{c.Configuration.Path}:{key}",
                                                         _s.Services,
                                                         out var created,
                                                         o => o.FindMissingFeature( _s.Services, p ) ) )
            {
                _options.Add( p, created );
            }
            else success = false;
        }
        return success;
    }

    // Called before the setup of the first registered driver of an asymmetric feature: a remote that declares only "XXX"
    // is an error ("XXX" is declared by a local party that is the feature). A local party can declare "XXXRemote": its
    // set contains the Remote side (for the remotes added later).
    internal bool CheckAsymmetricNames( IActivityMonitor monitor, IEnumerable<IParty> parties )
    {
        Throw.DebugAssert( _asymmetric != null );
        bool success = true;
        foreach( var p in parties )
        {
            if( p is ILocalParty ) continue;
            var c = GetConfiguration( p );
            var name = GetFeatureName( p );
            if( c.IsAllowedFeature( _featureName, false ) && !c.IsAllowedFeature( name, _isAllowedByDefault ) )
            {
                monitor.Error( $"'{p}' declares the '{_featureName}' feature: '{_featureName}' is declared in the \"Local\" of a local party that is the feature. The '{_featureName}' feature is asymmetric: "
                               + $"a remote that is the '{_featureName}' (or that uses the local '{_featureName}') declares '{name}'." );
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
    /// Called when a run-time tenant domain is removed (<see cref="IApplicationIdentityService.RemoveTenantInstanceAsync"/>):
    /// its identity is gone, the driver deletes what it kept for it in its private store that must not survive in a trash
    /// bin (the private keys of the party, for instance), or outside of it. The rest of the private store is then trashed.
    /// Does nothing by default.
    /// <para>
    /// The tenant domain is destroyed (or has never been loaded): it is designated by its full name and its private store,
    /// not by a party object. This is called in reverse order (from most dependent feature drivers to basic ones), in the
    /// agent's loop. An exception is logged: it doesn't prevent the other drivers to be called, the private store is not
    /// trashed then.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="fullName">The full name of the removed tenant domain.</param>
    /// <param name="localStore">Its private store (its "-Local").</param>
    /// <returns>The awaitable.</returns>
    protected internal virtual Task ForgetLocalPartyAsync( IActivityMonitor monitor, NormalizedPath fullName, IFileStore localStore ) => Task.CompletedTask;

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
        return _s._describer.RefreshAsync( monitor, party );
    }
}
