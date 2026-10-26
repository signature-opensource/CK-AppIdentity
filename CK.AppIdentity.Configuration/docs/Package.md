The configuration of an application identity: what you write in the "CK-AppIdentity" section, and the immutable objects it becomes.
ApplicationIdentityServiceConfiguration is the root: it holds RemotePartyConfiguration and TenantDomainPartyConfiguration, and with the
tenant domains an ApplicationIdentityLocalConfiguration (the "Local" section). Every level carries the inherited AllowFeatures,
DisallowFeatures and AssemblyConfiguration. Built on CK.Configuration.
