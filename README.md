# CK-AppIdentity

The goal of this library is to provide a minimal model of an application and its peers
and to support extensibility thanks to simple "features" that can be associated to the
identity objects.

Application identity may be the only aspect that requires an explicit configuration.
Any other aspects can have a default behavior, but the remote parties with whom an application
interact and how they interact can hardly exist without configuration

The initial objects are defined by a standard [.Net configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration)
that is locked and cannot be changed during the application lifetime. Configured objects are immutable
but one can dynamically define new objects and destroy dynamically defined objects.


## CK.AppIdentity
Contains the core objects:
- AppIdentityService is the root type. It is a singleton service that carries the 
  application identity and the remote parties.
- The [ApplicationIdentityFeatureDriver](CK.AppIdentity/Features/ApplicationIdentityFeatureDriver.cs) is the base class
  to implement in order to manage features on the Application identity objects.

## CK.AppIdentity.Hosting
This small library implements initialization of the identity configuration. It provides
a single extension method to `IHostBuilder` that injects a configured instance of `ApplicationIdentityServiceConfiguration`
as a singleton service in the DI container and initializes `CK.Core.CoreApplicationIdentity`.


