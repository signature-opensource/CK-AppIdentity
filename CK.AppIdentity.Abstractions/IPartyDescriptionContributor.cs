using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// A contribution of a package to the <see cref="ILocalParty.Description"/> of the root party: the HTTP endpoints give
/// the public URL (when "Local:PublicUrl" is not configured), the client applications give their names and request paths.
/// </summary>
[IsMultiple]
public interface IPartyDescriptionContributor : ISingletonAutoService
{
    /// <summary>
    /// Called once, when the host has started (the static features, like the HTTP endpoints, are started). The
    /// contributor calls <paramref name="onChanged"/> when its contribution changes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="onChanged">The callback to call when the contribution changes. It can be called from any thread.</param>
    void Activate( IActivityMonitor monitor, Action onChanged );

    /// <summary>
    /// Contributes to the description. This is called before <see cref="Activate"/> (when the party is set up) and
    /// after each change.
    /// </summary>
    /// <param name="builder">The description builder.</param>
    void Contribute( PartyDescription.Builder builder );
}
