using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Why a run-time tenant domain (an instance of an Opened <see cref="TenantDomainTemplate"/>) is not loaded at start.
/// </summary>
public enum UnloadedTenantReason
{
    /// <summary>
    /// Its record ("-Local/Instance.json") can't be read, or its name is not a valid instance name.
    /// </summary>
    InvalidRecord,

    /// <summary>
    /// Its index is above the "MaxInstanceCount" of its template: the configuration lowered it.
    /// </summary>
    IndexAboveBound,

    /// <summary>
    /// Its index is the one of a configured instance (the configuration added instances) or of a tenant loaded before it.
    /// </summary>
    IndexTaken,

    /// <summary>
    /// It runs in another process: its private store is locked.
    /// </summary>
    HeldByAnotherProcess,

    /// <summary>
    /// Its creation failed (see the logs).
    /// </summary>
    CreationFailed
}

/// <summary>
/// A run-time tenant domain that is not loaded: the configuration contradicts its record, or it can't be created. It is
/// kept, never trashed (its private store holds its keys): <see cref="IApplicationIdentityService.SetTenantIndexAsync"/>
/// moves it to a free index (it is then created), <see cref="IApplicationIdentityService.RemoveTenantInstanceAsync"/>
/// removes it.
/// </summary>
public sealed class UnloadedTenant
{
    /// <summary>
    /// Initializes a new unloaded tenant.
    /// </summary>
    /// <param name="template">The template of the tenant.</param>
    /// <param name="name">The name of the instance.</param>
    /// <param name="index">The index read in its record, null when it can't be read.</param>
    /// <param name="reason">Why it is not loaded.</param>
    /// <param name="message">A readable explanation.</param>
    public UnloadedTenant( TenantDomainTemplate template, string name, int? index, UnloadedTenantReason reason, string message )
    {
        Throw.CheckNotNullArgument( template );
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckNotNullArgument( message );
        Template = template;
        Name = name;
        FullName = template.GetFullName( name );
        Index = index;
        Reason = reason;
        Message = message;
    }

    /// <summary>
    /// Gets the template of this tenant.
    /// </summary>
    public TenantDomainTemplate Template { get; }

    /// <summary>
    /// Gets the name of the instance.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the full name of the tenant domain.
    /// </summary>
    public NormalizedPath FullName { get; }

    /// <summary>
    /// Gets the index read in its record, null when it can't be read.
    /// </summary>
    public int? Index { get; }

    /// <summary>
    /// Gets why this tenant is not loaded.
    /// </summary>
    public UnloadedTenantReason Reason { get; }

    /// <summary>
    /// Gets a readable explanation.
    /// </summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => $"'{FullName}' ({Reason}): {Message}";
}
