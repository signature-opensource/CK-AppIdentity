namespace CK.AppIdentity;

/// <summary>
/// Finds the party through which a call entered (the <c>CallerPartyId</c> of its authentication information).
/// </summary>
public static class CallerPartyExtensions
{
    /// <summary>
    /// Finds the caller party of a call received by this local party: the local party itself, or one of its remotes.
    /// <para>
    /// A caller party identifier of 0 is a direct reception by this local party whose <see cref="IParty.PartyActorId"/> is
    /// not known (the party model is not installed, or not resolved yet): a call relayed by a remote always carries the
    /// remote's identifier (a remote whose identifier is 0 cannot be a caller party). So 0 is this local party.
    /// </para>
    /// </summary>
    /// <param name="local">The local party that received the call.</param>
    /// <param name="callerPartyId">The caller party identifier (its PartyActorId).</param>
    /// <returns>The caller party, or null when it is not this local party nor one of its remotes.</returns>
    public static IParty? FindCallerParty( this ILocalParty local, int callerPartyId )
    {
        if( callerPartyId == 0 || callerPartyId == local.PartyActorId ) return local;
        foreach( var r in local.Remotes )
        {
            if( r.PartyActorId == callerPartyId ) return r;
        }
        return null;
    }
}
