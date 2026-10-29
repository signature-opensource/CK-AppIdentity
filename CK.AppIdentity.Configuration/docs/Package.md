The configuration of an application identity: what you write in the "CK-AppIdentity" section, and the immutable objects it becomes.
ApplicationIdentityServiceConfiguration is the root: it holds RemotePartyConfiguration and TenantDomainPartyConfiguration, and with the
tenant domains an ApplicationIdentityLocalConfiguration (the "Local" section). The parties (a "Local", a remote) carry their AllowFeatures,
DisallowFeatures and AssemblyConfiguration, resolved along the "Defaults" sections of the levels above them. Built on CK.Configuration.
