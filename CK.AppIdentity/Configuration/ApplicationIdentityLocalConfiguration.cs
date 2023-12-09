using CK.Core;

namespace CK.AppIdentity
{
    /// <summary>
    /// Captures the "Local" configuration of <see cref="ILocalParty.LocalConfiguration"/>.
    /// </summary>
    public sealed class ApplicationIdentityLocalConfiguration : ApplicationIdentityConfiguration
    {
        internal ApplicationIdentityLocalConfiguration( ImmutableConfigurationSection configuration, ref InheritedConfigurationProps props )
            : base( configuration, ref props )
        {
        }
    }
}
