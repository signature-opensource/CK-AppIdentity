using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Provides the default <see cref="CurrentLocalParty"/>: its <see cref="CurrentLocalParty.LocalParty"/> is
/// the root <see cref="IApplicationIdentityService"/>.
/// </summary>
public sealed class CurrentLocalPartyDefaultProvider : IAmbientServiceDefaultProvider<CurrentLocalParty>
{
    readonly CurrentLocalParty _default;

    /// <summary>
    /// Initializes a new default provider: the root <see cref="IApplicationIdentityService"/> is
    /// the default <see cref="CurrentLocalParty"/>.
    /// </summary>
    /// <param name="appIdentity">The root application identity service.</param>
    public CurrentLocalPartyDefaultProvider( IApplicationIdentityService appIdentity )
    {
        _default = new CurrentLocalParty( appIdentity );
    }

    /// <inheritdoc />
    public CurrentLocalParty Default => _default;
}
