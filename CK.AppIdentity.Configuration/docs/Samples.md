# Configuration samples

These samples introduce the basic concepts of the CK.AppIdentity configuration, step by step.
The configuration keys are listed in the [CK.AppIdentity.Configuration README](../README.md).

## The simplest one
An application identity is a triple: DomainName, PartyName, EnvironmentName that are strictly defined
(see [CK.Core.CoreApplicationIdentity](https://github.com/signature-opensource/CK-Core/tree/stable/CK.Core/CoreApplicationIdentity)). The simplest valid configuration is an application without any parties:
```json
{
  "FullName": "AcmeCorp/$Trolley1/#Production"
}
```
This is a functional but lonely application. Dynamic parties can then be added to it at runtime and
subsequently destroyed.

In the following examples, Parties are defined statically, in the initial configuration: these are not dynamic
parties and cannot be destroyed.

## Standard ones
A simple application that interacts with 2 remote parties:
```json
{
  "FullName": "AcmeCorp/$Trolley1/#Production",
  "Parties": [
   {
    "DomainName": "CentralHealth/Clients",
    "PartyName": "LogTower",
    "Address": "148.54.11.18:3712"
   },
   {
     "PartyName": "$SigBox",
     "Address": "155.88.22.22"
   }]
}
```
There should be no more than that: Trolley1 sends its logs to a LogTower and can initiate communications
(see below) with the SigBox.

The LogTower is not in the AcmeCorp domain but in the "CentralHealth/Clients" domain. The above example uses
the explicit properties "DomainName" and "PartyName". We could have used the
`"FullName": "CentralHealth/Clients/$LogTower"` here.

The SigBox also sends its logs to the same LogTower and knows the Trolley1 but also the Trolley2 and the PickingStation.
```json
{
  "FullName": "AcmeCorp/$SigBox/#Production",
  "Parties": [
  {
    "FullName": "CentralHealth/Clients/$LogTower",
    "Address": "148.54.11.18:3712"
  },
  {
    "PartyName": "Trolley1"
  },
  {
    "PartyName": "Trolley2"
  },
  {
    "PartyName": "PickingStation"
  }]
}
```
These 3 remotes have no Addresses: "AcmeCorp/$SigBox/#Production" is a server for these parties
and this is enough for the SigBox, 2 trolleys and one picking station to work together.

Now we want the SigBox to interact with a OneCS application (the supervision and operation portal).
The OneCS application typically lives in the cloud. If the SigBox can be reached from the outside,
we just need to declare the new OneCS remote on the SigBox.

_Notes:_
- From now on, we don't show the LogTower configuration. This is the same for every party
  (if we want to target the same LogTower).
- We also don’t specify the EnvironmentName anymore. For the application itself, this defaults to the
  IHostEnvironment.EnvironmentName prefixed with '#': "Development" becomes "#Dev" but a default .NET host
  is in "Production", which gives "#Production" (where `StrictConfigurationMode` is on). The parties inherit
  the EnvironmentName of their parent.

The name of this new Party is the same as the DomainName: **"AcmeCorp/$AcmeCorp"**.

When the PartyName is the same as the last segment of the Domain path, this Party conventionally designates the
"domain controller" which is the "default application" (we can call it "$Portal" or "$Root" or "$Main", etc.).
For example, the international customer BigCorp may have a root application (the world-wide portal) "BigCorp/$BigCorp"
and multiple country-specific applications "BigCorp/FR/$FR", "BigCorp/EN/$EN", etc.

This convention is simple and allows such "controllers" to be designated in a complex architecture. This is a
convention until one need to handle multi-tenancy: a **TenantDomain** That will be presented next follow
this convention (and have no Address).

```json
{
  "FullName": "AcmeCorp/$SigBox",
  "Parties": [
  {
    "PartyName": "$Trolley1"
  },
  {
    "PartyName": "$Trolley2"
  },
  {
    "PartyName": "$PickingStation"
  },
  {
    "PartyName": "$AcmeCorp"
  }]
}
```
Note that, declared this way (no Address), "$AcmeCorp" matches the TenantDomain definition presented
below: the SigBox analyzes it as a TenantDomain, not as a remote party. It is a remote party once it has an
Address, as in the second SigBox configuration below.

Below is the OneCS configuration:
```json
{
  "FullName": "AcmeCorp/$AcmeCorp",
  "Parties": [
  {
    "PartyName": "$SigBox",
    "Address": "65.12.13.14"
  }]
}
```
If, for any reason, the SigBox cannot be reached from the outside (typically because of a firewall), we simply
change the place where the Address is defined. By moving the Address from the OneCS configuration to the SigBox,
the box becomes the initiator (the Client) and OneCS becomes a listener (a Server) for the box.

The configurations become:
```json
{
  "FullName": "AcmeCorp/$AcmeCorp",
  "Parties": [
  {
    "PartyName": "$SigBox"
  }]
}
```
And:
```json
{
  "FullName": "AcmeCorp/$SigBox",
  "Parties": [
  {
    "PartyName": "$Trolley1"
  },
  {
    "PartyName": "$Trolley2"
  },
  {
    "PartyName": "$PickingStation"
  },
  {
    "PartyName": "$AcmeCorp",
    "Address": "27.28.29.30"
  }]
}
```

## Multi-tenancy support: the TenantDomain configuration

A **Tenant** is a subset of bigger system logically tied to some human concept like an organization or a "group
or resources" for which data specific to the Tenant must be handled potentially by code specific to that Tenant.

A multi-tenant application hosts one or more Tenants, each being logically disjointed from the others. The Application
identity models supports such Tenants definition through the **TenantDomain** concept. A TenantDomain is a Party
of an application (just like a Remote Party is) but it is a Party that contains other Parties. Defining a **TenantDomain**
is straightforward:
- Its name follow the "controller domain" convention (its full name is like "BigCorp/$BigCorp" or "BigCorp/EN/$EN").
- It has no Address:
  - It can be somehow "virtual": no one will call it, it has no intrinsic capability;
  - Or it can play an active part in the "domain it controls" and then it is necessarily a Server for its Remote Parties.

The important point to understand here is that this structure is not recursive; it's not a tree structure. A Tenant
is a simple group with a list of Remote Parties. Usually its Remote Parties have the same Domain as the Tenant's one, or
are in a sub domain of the Tenant's Domain but this is not mandatory.

The simplest TenantDomain definition (an item of the "Parties") is:
```json
{
  "FullName": "AcmeCorp/$AcmeCorp"
}
```
It is initially empty but dynamic remotes can be programmatically added to it.

Because each Tenant has its own dedicated configuration space, its "Defaults" section can hold the default values of
its parties: its "Local" and its Remotes (and only them). See below.

## The "Local" and the "Defaults" configurations

The root, a tenant domain and a group of parties are *levels*: their own keys are only their identity and their
structure (the names, the "Parties", "Local" and "Defaults"). A key that configures something cannot be written on a
level: it is an error. It goes in one of these sections:

- **"Local"** is the configuration of the local application (or of the tenant): its features, its assemblies, the
  settings of its components.
- **"Defaults"** holds the default values of all the parties below the level: its "Local" and its remotes. A party's own
  key always wins, otherwise the nearest "Defaults" above it applies.

```jsonc
{
  "FullName": "Test/$App",
  // Default values for every party below (the "Local" included).
  "Defaults": {
     "AllowFeatures": "KeyManagement"
  },
  // The configuration of the application itself.
  "Local": {
     "AllowFeatures": "AuthCenter"
  },
  "Parties": [
   {
     "PartyName": "$Box"
   }]
}
```

The application has the KeyManagement and AuthCenter features, the Box only the KeyManagement. Allowing a feature in
the "Local" concerns only the application: nothing written in a "Local" applies to the remotes.

In a multi-tenant configuration, each Tenant has its "Local" and its "Defaults":

```jsonc
{
  "FullName": "Test/$SaaSApp",
  "Defaults": {
     // For every party of the configuration, the tenants' included.
  },
  "Local": {
     // The local application.
  },
  "Parties": [
   {
     "FullName": "Tenant1/$Tenant1",
     "Defaults": {
        // For the parties of Tenant1 (its "Local" and its remotes): they win over the root's "Defaults".
     },
     "Local": {
        // The local configuration of Tenant1.
     },
     "Parties": [
      {
        "PartyName": "$Box",
        "Address": "126.87.1.1:4040"
      }]
   }]
}
```

A group of parties (an item of the "Parties" with no PartyName) can also have a "Defaults" that applies to its parties:
this factorizes their common configuration. A group is only a configuration convenience: its parties are "flattened",
a Tenant is and remains a simple list of Remote Parties.

The domain and environment names are the only keys that a level passes to its parties: they are part of their identity
(a party in a tenant is in the tenant's domain).
