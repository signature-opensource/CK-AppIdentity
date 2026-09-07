One extension method on IHostApplicationBuilder that wires the application identity into a host.

It reads the "CK-AppIdentity" configuration section, registers the resulting
ApplicationIdentityServiceConfiguration as a singleton, and initializes CK.Core.CoreApplicationIdentity
from the same triplet, using the command line as the default context descriptor.

Calling it twice is supported and the last context descriptor wins. If something initialized the core
identity first, that is a warning rather than a failure: the configuration is still registered.
