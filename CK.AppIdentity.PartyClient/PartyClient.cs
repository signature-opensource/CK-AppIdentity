using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// A client of a local party: an application that sends commands to the party on behalf of users (a browser application
/// served by the party, a mobile application, a local tool...). A client is not a party: it has no identity key and it
/// is not a remote.
/// <para>
/// A client is a bag of client features (<see cref="ClientFeature"/>), as a party is a bag of features: "NgApp" (an
/// Angular application), "Hosting" (served by its party)... It is declared by its party in its "Local:PartyClient:{Name}"
/// section, whose keys are its features (and its type set, compile time).
/// </para>
/// </summary>
public sealed class PartyClient
{
    IReadOnlyList<ClientFeature> _features;
    ClientApplicationDescription? _description;

    internal PartyClient( ILocalParty party, string name, ImmutableConfigurationSection configuration )
    {
        Party = party;
        Name = name;
        Configuration = configuration;
        _features = [];
    }

    /// <summary>
    /// Gets the local party of this client.
    /// </summary>
    public ILocalParty Party { get; }

    /// <summary>
    /// Gets the name of this client: unique in its <see cref="Party"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the "Local:PartyClient:{Name}" configuration section of this client.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <summary>
    /// Gets the features of this client, in their order (a feature comes after the ones it requires).
    /// </summary>
    public IReadOnlyList<ClientFeature> Features => _features;

    /// <summary>
    /// Gets the feature of a type (or of a type that implements it), or null.
    /// </summary>
    /// <typeparam name="T">The feature type, or an abstraction (<see cref="IWebAppBuild"/> for instance).</typeparam>
    /// <returns>The feature or null.</returns>
    public T? GetFeature<T>() where T : class => _features.OfType<T>().FirstOrDefault();

    /// <summary>
    /// Gets what this client states to the remotes, in the description of its party (<see cref="PartyDescription.ClientApplications"/>):
    /// its name, its request path and its facts, assembled from its features. Null when the party doesn't serve it (no
    /// feature gives a request path).
    /// </summary>
    public ClientApplicationDescription? Description => _description;

    internal void SetFeatures( IReadOnlyList<ClientFeature> features )
    {
        _features = features;
        var b = new ClientDescriptionBuilder( this );
        foreach( var f in features ) f.ContributeDescription( b );
        _description = b.RequestPath != null ? new ClientApplicationDescription( Name, b.RequestPath, b.Facts ) : null;
    }

    /// <inheritdoc />
    public override string ToString() => $"client '{Name}' of '{Party.FullName}'";
}
