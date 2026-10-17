using SharedKernel;

namespace Identity.Contracts;

/// <summary>
/// Housekeeping over accounts that were provisioned and never claimed — the tail of the lifecycle
/// <see cref="IUserProvisioning"/> starts. A login is created inactive and activated only when the
/// employee or client row it belongs to commits; if that never happens (the process died mid-command,
/// or the caller abandoned the flow), the account sits inactive and attached to nothing, forever.
///
/// <para><b>Its one legitimate consumer is <c>UnlinkedUserReaperJob</c>, in the Api layer.</b> Deciding
/// whether a user is linked means asking HR and CRM, which Identity may not do — it is a sink. Module
/// code resolving this interface is a design error even though the type is technically reachable from
/// anywhere <c>Identity.Contracts</c> is: enumerating and deleting accounts is not something HR or CRM
/// should be able to do as a side effect of being allowed to create one. That is why these two methods
/// are here and not on <see cref="IUserProvisioning"/>, which both of those modules reference.</para>
/// </summary>
public interface IUserLifecycleMaintenance
{
    /// <summary>Ids of users that are inactive and were created before <paramref name="createdBeforeUtc"/>,
    /// oldest first, at most <paramref name="limit"/> of them.
    ///
    /// <para>The limit is not decoration. In steady state the retention window bounds this set, but a
    /// stalled outbox leaves every new hire unactivated — which is exactly when the reaper runs and
    /// exactly when the set is unbounded. It also bounds the per-id directory lookups the caller makes
    /// for each result.</para>
    ///
    /// <para><b>Inactive does not mean unlinked.</b> These are candidates, not condemned: the set
    /// includes deactivated ex-employees, whose accounts must not be deleted. The caller is responsible
    /// for checking that nothing references them.</para></summary>
    Task<IReadOnlyList<string>> ListUnactivatedAsync(DateTime createdBeforeUtc, int limit, CancellationToken ct);

    /// <summary>Deletes a user that never became active. Refuses an active one outright, whatever the
    /// caller believes — a live login is never housekeeping.</summary>
    Task<Result> DeleteUnactivatedAsync(string userId, CancellationToken ct);
}
