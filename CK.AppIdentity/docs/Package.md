The implementation of the application identity model.
ApplicationIdentityService is the singleton hosted lifecycle service that implements IApplicationIdentityService: the local party
at the root, with dynamic parties added and destroyed at runtime. ApplicationIdentityFeatureDriver is the base class of the
feature drivers, and the public MicroAgent serializes every mutation.
