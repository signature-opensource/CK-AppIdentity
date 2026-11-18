using CK.Core;
using System.Threading.Tasks;
using System;
using CK.PerfectEvent;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace CK.AppIdentity;

/// <summary>
/// Root of the identity model. Contains all the identity objects.
/// </summary>
public interface IApplicationIdentityService : ILocalParty, ISingletonAutoService
{
    /// <summary>
    /// Gets the <see cref="ApplicationIdentityServiceConfiguration"/> object.
    /// </summary>
    new ApplicationIdentityServiceConfiguration Configuration { get; }

    /// <summary>
    /// Raised whenever a new party appears or disappears in <see cref="AllParties"/>.
    /// <para>
    /// By subscribing to this event one can track any structural
    /// change of the whole identity system.
    /// </para>
    /// </summary>
    PerfectEvent<IOwnedParty> AnyPartyChanged { get; }

    /// <summary>
    /// Gets the tenant domains that this application hosts.
    /// <para>
    /// This is a snapshot of the parties: while enumerating <see cref="IOwnedParty.IsDestroyed"/> may be
    /// true (or becomes true at any time).
    /// </para>
    /// </summary>
    IReadOnlyCollection<ITenantDomainParty> TenantDomains { get; }

    /// <summary>
    /// Gets the Opened tenant templates (see <see cref="TenantDomainTemplate"/>): their run-time instances are created by
    /// <see cref="AddTenantInstanceAsync"/>. Each one is a <see cref="TenantDomains"/> item whose
    /// <see cref="TenantDomainPartyConfiguration.IsDynamicInstance"/> is true, kept in the store by its record (its index,
    /// in "-Local/Instance.json") and created again at start.
    /// </summary>
    IReadOnlyList<TenantDomainTemplate> TenantTemplates { get; }

    /// <summary>
    /// Gets the run-time tenant domains that have not been loaded at start (the configuration contradicts their record, or
    /// they can't be created). They are kept: they are moved (<see cref="SetTenantIndexAsync"/>) or removed
    /// (<see cref="RemoveTenantInstanceAsync"/>).
    /// </summary>
    IReadOnlyList<UnloadedTenant> UnloadedTenants { get; }

    /// <summary>
    /// Creates a run-time tenant domain: an instance of an Opened tenant template, named by the caller. Its index is the
    /// first free one (no configured instance, no loaded tenant has it, up to the "MaxInstanceCount"). The tenant domain is
    /// set up (its features, its keys, its remotes), then its record is written: a failure leaves no record.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="template">One of the <see cref="TenantTemplates"/>.</param>
    /// <param name="name">
    /// The name of the instance (a valid instance name: see <see cref="ConfigurationTemplate.IsValidInstanceName"/>), not
    /// the name of a configured instance nor of an existing tenant (loaded or not).
    /// </param>
    /// <returns>The new tenant domain or null on error.</returns>
    Task<ITenantDomainParty?> AddTenantInstanceAsync( IActivityMonitor monitor, TenantDomainTemplate template, string name );

    /// <summary>
    /// Moves a run-time tenant domain (a loaded one or one of the <see cref="UnloadedTenants"/>) to another index: a free
    /// index of the run-time instances of its template. Its identity doesn't change (its full name, its keys), only its
    /// record; the values that use its index do: a loaded tenant domain is destroyed and created again.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tenantFullName">The full name of the tenant domain.</param>
    /// <param name="index">The new index.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> SetTenantIndexAsync( IActivityMonitor monitor, NormalizedPath tenantFullName, int index );

    /// <summary>
    /// Removes a run-time tenant domain (a loaded one or one of the <see cref="UnloadedTenants"/>): it is destroyed, the
    /// pins of its run-time remote instances are trashed, its keys are deleted (by the feature drivers) and what remains of
    /// its private store is trashed, its record with it. Its identity is gone: a new tenant of the same name is a new
    /// identity. A configured tenant domain is removed by the configuration.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tenantFullName">The full name of the tenant domain.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> RemoveTenantInstanceAsync( IActivityMonitor monitor, NormalizedPath tenantFullName );

    /// <summary>
    /// Gets a concatenation of the <see cref="ILocalParty.Remotes"/> directly owned by this application
    /// and the <see cref="TenantDomains"/>.
    /// <para>
    /// This is a snapshot of the parties: while enumerating <see cref="IOwnedParty.IsDestroyed"/> may be
    /// true (or becomes true at any time).
    /// </para>
    /// </summary>
    IEnumerable<IOwnedParty> Parties { get; }

    /// <summary>
    /// Gets the <see cref="ILocalParty.Remotes"/> directly owned by this application, and all remotes of the <see cref="TenantDomains"/>
    /// recursively (depth-first traversal).
    /// <para>
    /// This is a snapshot of the parties: while enumerating <see cref="IOwnedParty.IsDestroyed"/> may be
    /// true (or becomes true at any time).
    /// </para>
    /// </summary>
    IEnumerable<IRemoteParty> AllRemotes { get; }

    /// <summary>
    /// Gets the complete set of parties: the <see cref="ILocalParty.Remotes"/> owned by this application and
    /// the <see cref="TenantDomains"/> with their Remotes (depth-first traversal).
    /// <para>
    /// This is a snapshot of the parties: while enumerating <see cref="IOwnedParty.IsDestroyed"/> may be
    /// true (or becomes true at any time).
    /// </para>
    /// </summary>
    IEnumerable<IOwnedParty> AllParties { get; }

    /// <summary>
    /// Gets a task that is completed once all the <see cref="IApplicationIdentityFeatureDriver"/> have been
    /// initialized. Initialization errors are set on this task if exceptions occurred: awaiting this
    /// task will re-throw the initialization errors.
    /// <para>
    /// Use <see cref="Task.IsCompletedSuccessfully"/> to know if initialization has been successful.
    /// </para>
    /// <para>
    /// This task is faulted when the service refused to start (feature drivers registration issue)
    /// and canceled when the service has been stopped before being started.
    /// </para>
    /// </summary>
    Task InitializationTask { get; }

    /// <summary>
    /// Gets the <see cref="ISystemClock"/> that must be used by all code related to application identities.
    /// </summary>
    ISystemClock SystemClock { get; }

    /// <summary>
    /// Raises an approximative 1 second, non reentrant, heart beat signal.
    /// When a heartbeat (or any other agent job) takes longer than the period, the missed beats are skipped:
    /// they are not raised later.
    /// <para>
    /// The period can be changed by using a specialized <see cref="IApplicationIdentityService.SystemClock"/>.
    /// </para>
    /// </summary>
    PerfectEvent<int> Heartbeat { get; }
}
