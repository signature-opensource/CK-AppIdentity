One extension method, AddApplicationIdentityServiceConfiguration on IHostApplicationBuilder, that wires the application identity
into a host: it reads the "CK-AppIdentity" configuration section, registers the resulting ApplicationIdentityServiceConfiguration
as a singleton, and initializes CK.Core.CoreApplicationIdentity from it.
