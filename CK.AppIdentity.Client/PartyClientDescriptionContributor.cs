using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// Contributes the clients of the root party to its description (<see cref="PartyDescription.ClientApplications"/>):
/// the clients that have a <see cref="PartyClient.Description"/>.
/// <para>
/// The description contributors complete the root party's description only: the tenant domains' clients are not
/// described yet.
/// </para>
/// </summary>
public sealed class PartyClientDescriptionContributor : IPartyDescriptionContributor
{
    readonly ClientFeatureDriver _driver;

    /// <summary>
    /// Initializes a new <see cref="PartyClientDescriptionContributor"/>.
    /// </summary>
    /// <param name="driver">The client feature driver: it is set up before the description of the parties.</param>
    public PartyClientDescriptionContributor( ClientFeatureDriver driver )
    {
        _driver = driver;
    }

    /// <inheritdoc />
    public void Activate( IActivityMonitor monitor, Action onChanged )
    {
        // The clients of the root party are created by its setup: they never change.
    }

    /// <inheritdoc />
    public void Contribute( PartyDescription.Builder builder )
    {
        var feature = _driver.ApplicationIdentityService.GetFeature<ClientFeature>();
        if( feature == null ) return;
        foreach( var c in feature.Clients )
        {
            if( c.Description is { } d ) builder.AddClientApplication( d.Name, d.RequestPath );
        }
    }
}
