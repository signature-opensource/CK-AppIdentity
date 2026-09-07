The implementation of the application identity model.

ApplicationIdentityService is a singleton hosted lifecycle service and is itself the local party, so the
application's own identity is the root object rather than an entry inside one. Configured parties are
immutable; dynamic ones can be added and destroyed at runtime through the same configuration shape.

Thread safety comes from a micro agent that serializes every mutation - initialization, party lifetime,
feature setup and teardown, disposal - rather than from locking. That agent's base class is public, for
features that need a serialization point of their own.
