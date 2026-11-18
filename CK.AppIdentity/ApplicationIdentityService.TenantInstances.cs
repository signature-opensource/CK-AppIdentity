using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.AppIdentity;

// The run-time tenant domains (the instances of the Opened tenant templates): see AppIdentityAgent.TenantInstances.
public sealed partial class ApplicationIdentityService
{
    // Set by the agent (at start, by a move or a removal).
    internal volatile UnloadedTenant[] _unloadedTenants = Array.Empty<UnloadedTenant>();

    /// <inheritdoc />
    public IReadOnlyList<TenantDomainTemplate> TenantTemplates => Configuration.TenantTemplates;

    /// <inheritdoc />
    public IReadOnlyList<UnloadedTenant> UnloadedTenants => _unloadedTenants;

    /// <inheritdoc />
    public async Task<ITenantDomainParty?> AddTenantInstanceAsync( IActivityMonitor monitor, TenantDomainTemplate template, string name )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( template );
        Throw.CheckNotNullArgument( name );
        if( !Configuration.TenantTemplates.Contains( template ) )
        {
            monitor.Error( $"The tenant template {template} is not a template of '{FullName}'." );
            return null;
        }
        name = name.Trim();
        if( !ConfigurationTemplate.IsValidInstanceName( name ) )
        {
            monitor.Error( $"Invalid tenant name '{name}': a name is made of letters, digits, '-' and '_' (and doesn't start with a digit, '-' or '_')." );
            return null;
        }
        if( template.Instances.IsConfiguredName( name ) )
        {
            monitor.Error( $"'{name}' is a configured instance of the tenant template {template}." );
            return null;
        }
        TenantDomainParty? created = null;
        await _agent.RunInLoopAsync( async m =>
        {
            created = await _agent.CreateNewTenantAsync( m, template, name ).ConfigureAwait( false );
            return created != null;
        } ).ConfigureAwait( false );
        if( created == null ) monitor.Error( $"Unable to create the tenant '{name}' of the template {template} (see the logs of '{FullName}')." );
        return created;
    }

    /// <inheritdoc />
    public async Task<bool> SetTenantIndexAsync( IActivityMonitor monitor, NormalizedPath tenantFullName, int index )
    {
        Throw.CheckNotNullArgument( monitor );
        if( !FindRuntimeTenant( monitor, tenantFullName, out var loaded, out var template, out var name ) ) return false;
        if( !template.Instances.IsDynamicIndex( index ) )
        {
            monitor.Error( $"The index {index} is not an index of the run-time instances of the template {template}: {template.Instances.Count + 1} to {template.Instances.MaxInstanceCount}." );
            return false;
        }
        int? previous = loaded?.Configuration.Instance!.Index;
        if( previous == index ) return true;
        // The destruction is a job of the agent: it is awaited before the move (in the loop).
        if( loaded != null ) await loaded.DestroyAsync().ConfigureAwait( false );
        if( await _agent.RunInLoopAsync( m => _agent.MoveTenantAsync( m, template, name, index, previous ) ).ConfigureAwait( false ) )
        {
            return true;
        }
        monitor.Error( $"Unable to move the tenant '{tenantFullName}' to the index {index} (see the logs of '{FullName}')." );
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveTenantInstanceAsync( IActivityMonitor monitor, NormalizedPath tenantFullName )
    {
        Throw.CheckNotNullArgument( monitor );
        if( !FindRuntimeTenant( monitor, tenantFullName, out var loaded, out var template, out var name ) ) return false;
        if( loaded != null ) await loaded.DestroyAsync().ConfigureAwait( false );
        if( await _agent.RunInLoopAsync( m => _agent.ForgetTenantAsync( m, template, name ) ).ConfigureAwait( false ) )
        {
            return true;
        }
        monitor.Error( $"Unable to remove the tenant '{tenantFullName}' (see the logs of '{FullName}')." );
        return false;
    }

    // A run-time tenant: a loaded tenant domain that is a run-time instance, or an unloaded one.
    bool FindRuntimeTenant( IActivityMonitor monitor,
                            NormalizedPath fullName,
                            out TenantDomainParty? loaded,
                            out TenantDomainTemplate template,
                            out string name )
    {
        loaded = _domains.FirstOrDefault( d => d.FullName.Path.Equals( fullName.Path, StringComparison.OrdinalIgnoreCase ) );
        if( loaded != null && loaded.Configuration.IsDynamicInstance && !loaded.IsDestroyed )
        {
            template = loaded.Configuration.Template!;
            name = loaded.Configuration.Instance!.Name;
            return true;
        }
        loaded = null;
        var u = _unloadedTenants.FirstOrDefault( u => u.FullName.Path.Equals( fullName.Path, StringComparison.OrdinalIgnoreCase ) );
        if( u != null )
        {
            template = u.Template;
            name = u.Name;
            return true;
        }
        monitor.Error( $"'{fullName}' is not a run-time tenant of '{FullName}' (a configured tenant domain is changed by the configuration)." );
        template = null!;
        name = null!;
        return false;
    }
}
