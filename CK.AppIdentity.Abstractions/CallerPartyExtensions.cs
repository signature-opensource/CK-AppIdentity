namespace CK.AppIdentity;

/// <summary>
/// Finds the party through which a call entered (the <c>CallerPartyFullName</c> of its authentication information).
/// </summary>
public static class CallerPartyExtensions
{
    /// <summary>
    /// Finds the caller party of a call received by this local party: the local party itself, or one of its remotes.
    /// The full names are compared with <see cref="System.StringComparison.Ordinal"/>.
    /// </summary>
    /// <param name="local">The local party that received the call.</param>
    /// <param name="callerPartyFullName">The full name of the caller party. Null when it is unknown or not applicable.</param>
    /// <returns>The caller party, or null when it is unknown, not this local party nor one of its remotes.</returns>
    public static IParty? FindCallerParty( this ILocalParty local, string? callerPartyFullName )
    {
        if( callerPartyFullName == null ) return null;
        if( local.FullName.Path == callerPartyFullName ) return local;
        foreach( var r in local.Remotes )
        {
            if( r.FullName.Path == callerPartyFullName ) return r;
        }
        return null;
    }
}
