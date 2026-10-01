using CK.Core;
using System.Threading.Tasks;

namespace CK.AppIdentity;

interface IOwnedPartyInternal : IOwnedParty
{
    new LocalParty Owner { get; }

    /// <summary>
    /// Completes the <see cref="IOwnedParty.DestroyAsync"/> task. This can be called multiple times.
    /// For a tenant domain, this also signals its remotes.
    /// </summary>
    void SignalDestroyed();
}
