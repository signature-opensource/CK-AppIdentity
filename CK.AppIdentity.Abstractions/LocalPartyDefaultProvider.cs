using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Provides the default <see cref="ILocalParty"/> that is the root <see cref="IApplicationIdentityService"/>.
/// </summary>
public sealed class LocalPartyDefaultProvider : IAmbientServiceDefaultProvider<ILocalParty>
{
    readonly IApplicationIdentityService _appIdentity;

    /// <summary>
    /// Initializes a new default provider: the root <see cref="IApplicationIdentityService"/> is
    /// the default <see cref="ILocalParty"/>.
    /// </summary>
    /// <param name="appIdentity">The root application identity service.</param>
    public LocalPartyDefaultProvider( IApplicationIdentityService appIdentity )
    {
        _appIdentity = appIdentity;
    }

    /// <inheritdoc />
    public ILocalParty Default => _appIdentity;
}
