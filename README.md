# CK-AppIdentity

The goal of this library is to provide a minimal model of an application and its peers
and to support extensibility thanks to simple "features" that can be associated to the
identity objects AND to minimize the "configuration mess".

Application identity may be the only aspect that requires an explicit configuration.
Any other aspects can have a default behavior, but the remote parties with whom an application
interact and how they interact can hardly exist without configuration.

The initial objects are defined by a standard [.NET configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration)
that is locked and cannot be changed during the application lifetime. Configured objects are immutable
but one can dynamically define new objects and destroy dynamically defined objects. CK.AppIdentity
relies on [CK.Configuration](https://github.com/signature-opensource/CK-Configuration/blob/master/CK.Configuration/README.md)
to handle .NET configurations objetcs.

## CK.AppIdentity
Contains the core objects:
- AppIdentityService is the root type. It is a singleton service that carries the 
  application identity and the remote parties.
- The [ApplicationIdentityFeatureDriver](CK.AppIdentity/Features/ApplicationIdentityFeatureDriver.cs) is the base class
  to implement in order to manage features on the Application identity objects.

To understand this package, please read (in this order):
1. [Application identity model configuration](CK.AppIdentity/Configuration/README.md) introduces
   Application Identity through its configuration.
2. [Application identity model](CK.AppIdentity/Model/README.md) describes the Application Identity
   objects built from their configurations.
3. [The Features](CK.AppIdentity/Features/README.md) eventually presents the real meat of identity objects.

## CK.AppIdentity.Hosting
This small library implements initialization of the identity configuration. It provides
a single extension method to `IHostBuilder` that injects a configured instance of `ApplicationIdentityServiceConfiguration`
as a singleton service in the DI container and initializes `CK.Core.CoreApplicationIdentity`.


