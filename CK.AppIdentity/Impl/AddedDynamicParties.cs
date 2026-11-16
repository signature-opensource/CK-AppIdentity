using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// Captures the result of a dynamic configuration processing: the remotes added by
/// <see cref="ILocalParty.AddMultipleRemotesAsync"/> or <see cref="ILocalParty.AddRemoteAsync"/>, or the tenant domain
/// added by the internal <see cref="ApplicationIdentityService.AddTenantDomainAsync"/>.
/// </summary>
/// <param name="Remotes">The list of added remotes.</param>
/// <param name="Tenants">The list of added tenant domains.</param>
readonly record struct AddedDynamicParties( IReadOnlyCollection<IRemoteParty> Remotes, IReadOnlyCollection<ITenantDomainParty> Tenants )
{
    /// <summary>
    /// Gets the remotes and tenants count.
    /// </summary>
    public int Count => Remotes.Count + Tenants.Count;

    /// <summary>
    /// Gets the remotes and the tenants.
    /// </summary>
    public IEnumerable<IOwnedParty> Parties => ((IEnumerable<IOwnedParty>)Remotes).Concat( Tenants );
}
