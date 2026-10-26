The running model of an application identity, built from its configuration.
IParty and its specializations IOwnedParty, ILocalParty, IRemoteParty and ITenantDomainParty; IApplicationIdentityService is the
local party at the root. Every party exposes an IFileStore (SharedFileStore, and LocalFileStore for local parties) and carries
features (AddFeature, GetFeature, GetRequiredFeature).
