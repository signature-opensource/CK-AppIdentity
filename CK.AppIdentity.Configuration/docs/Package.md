The configuration of an application identity: what you write in the "CK-AppIdentity" section, and the immutable objects
it becomes. Built on CK.Configuration.

`ApplicationIdentityServiceConfiguration` is the root: it holds `RemotePartyConfiguration` and `TenantDomainPartyConfiguration`, and with the
tenant domains an `ApplicationIdentityLocalConfiguration` (the "Local" section). The parties (a "Local", a remote) declare their features by their keys
("AuthCenter": true, or a section; false turns off a feature allowed by default) and carry their `AssemblyConfiguration`, resolved along the "Defaults" sections of the levels above them.
