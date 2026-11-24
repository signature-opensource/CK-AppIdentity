Wires the application identity into a host: `AddApplicationIdentityServiceConfiguration` reads the "CK-AppIdentity"
configuration section of a `HostApplicationBuilder`.

It registers the resulting `ApplicationIdentityServiceConfiguration` as a singleton, and initializes
`CK.Core.CoreApplicationIdentity` from it.
