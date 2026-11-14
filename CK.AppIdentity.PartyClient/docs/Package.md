The "PartyClient" feature of CK.AppIdentity: the clients of a party (the applications that send commands to it on behalf of
users). A client (PartyClient) is a bag of client features (ClientFeature, created by their ClientFeatureDriver<T>), declared
by a party in its "Local:PartyClient:{Name}" configuration section. PartyClientFeatureDriver creates them on the local
parties (PartyClientFeature), checks the requirements of their features and announces them in the description of each
local party. IWebAppBuild is the web build of a client.
