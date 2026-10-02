using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// The automatic ambient <see cref="ILocalParty"/> scoped service.
/// </summary>
/// <remarks>
/// This envelope is required because the local <see cref="IApplicationIdentityService"/> that is
/// a <see cref="ILocalParty"/> is also a <see cref="ISingletonAutoService"/>: it cannot also be
/// a <see cref="IScopedAutoService"/>.
/// </remarks>
public sealed record CurrentLocalParty( ILocalParty LocalParty ) : IAmbientAutoService;

