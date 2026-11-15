using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Captures the "Local" configuration of <see cref="ILocalParty.LocalConfiguration"/>.
/// </summary>
public sealed class ApplicationIdentityLocalConfiguration : ApplicationIdentityConfiguration
{
    /// <summary>
    /// The key of the public URL of a local party: the URL it states in its description (see <c>PartyDescription.PublicUrl</c>),
    /// when its HTTP endpoints are not reached directly (a reverse proxy).
    /// </summary>
    public const string PublicUrlKey = "PublicUrl";

    internal ApplicationIdentityLocalConfiguration( ImmutableConfigurationSection configuration, ref ResolvedConfigurationProps props )
        : base( configuration, ref props )
    {
    }

    /// <inheritdoc />
    public override bool IsPartyKey( string key ) => StructuralKeys.LocalPartyKeys.Contains( key );
}
