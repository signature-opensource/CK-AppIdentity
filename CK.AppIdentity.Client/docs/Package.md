The "Client" feature of CK.AppIdentity: the clients of a party (the applications that send commands to it on behalf of
users). PartyClient is the base class of a kind of client; IPartyClientFactory is implemented by the package that builds
the clients of a kind; a party declares the clients it uses in its "Local:Client:{Name}" configuration section.
ClientFeatureDriver creates them on the local parties (ClientFeature), and PartyClientDescriptionContributor announces
them in the description of the root party.
