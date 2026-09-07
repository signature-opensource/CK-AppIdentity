The running model of an application identity, built from its configuration.

One interface, IParty, with four specializations: a party can be owned, local, or both. A remote party
carries an address, a tenant domain carries remotes of its own, and the application identity service is
the local party at the root.

Every party exposes a SharedFileStore - a real directory, shared by all applications using CK-AppIdentity
on the machine - and every local party a LocalFileStore inside it. Parties do nothing by themselves:
features are attached to them and carry the actual behaviour.
