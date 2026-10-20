using BuildingBlocks.Jobs;
using Crm.Contracts;
using Hr.Contracts;
using Identity.Contracts;
using SharedKernel;

namespace Api.Jobs;

/// <summary>
/// Deletes logins that were provisioned and never claimed: created inactive by
/// <see cref="IUserProvisioning"/>, still inactive past the retention threshold, and referenced by
/// neither an employee nor a client. These are the residue of a hire or a client creation that died
/// between provisioning the account and committing the row it belonged to.
///
/// <para><b>Why this lives in the Api layer and not in Identity.</b> Deciding whether a user is linked
/// means asking <see cref="IHrDirectory"/> and <see cref="IClientDirectory"/>, and Identity is a sink —
/// it references no other module, which <c>IdentityContractsIsolationTests</c> now enforces. Api is the
/// composition root and the one place where reaching across modules is legitimate. This deviates
/// knowingly from <c>PeriodicJobRegistration</c>'s per-module convention; the deviation is the boundary
/// working, not a shortcut around it.</para>
///
/// <para><b><c>IsActive</c> carries two meanings now, and this is why the directory checks are not
/// optional.</b> It means "deactivated ex-employee" and it means "provisioned, never linked". They
/// compose correctly for authentication — both mean the person may not log in — but they are opposite
/// for deletion: one is a record to keep, the other is litter. Reaping on <c>IsActive</c> alone would
/// delete every ex-employee's account and take their audit trail's readability with it.</para>
///
/// <para><b>Orphaned audit rows are left behind, by design.</b> <c>ApplicationUser</c> is
/// <c>IAuditable</c>, so provisioning wrote an audit row naming the user this job deletes.
/// <c>audit_logs.UserId</c> is a plain string with no foreign key (<c>shared</c> sits outside the
/// cross-schema allow-list), so the row survives as a dangling id rather than erroring. The audit trail
/// is deliberately not cascaded: history is a record of what happened, and the provisioning did
/// happen.</para>
/// </summary>
internal sealed class UnlinkedUserReaperJob(
    IUserLifecycleMaintenance maintenance,
    IHrDirectory hrDirectory,
    IClientDirectory clientDirectory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<UnlinkedUserReaperJob> logger) : IPeriodicJob
{
    internal const string RetentionDaysKey = "Provisioning:UnlinkedUserRetentionDays";
    internal const string BatchSizeKey = "Provisioning:ReaperBatchSize";

    private const int DefaultRetentionDays = 7;
    private const int DefaultBatchSize = 100;

    public string Name => nameof(UnlinkedUserReaperJob);

    public async Task RunAsync(CancellationToken ct)
    {
        int retentionDays = configuration.GetValue(RetentionDaysKey, DefaultRetentionDays);
        int batchSize = configuration.GetValue(BatchSizeKey, DefaultBatchSize);

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime cutoff = now.AddDays(-retentionDays);

        // The batch size is what bounds this sweep, and it bounds the two directory calls per candidate
        // below with it. IClientDirectory.ResolveByAppUserIdAsync has no batch sibling - a gap worth
        // closing, and noted, but not one that matters while the loop itself is capped.
        IReadOnlyList<string> candidates = await maintenance.ListUnactivatedAsync(cutoff, batchSize, ct);
        if (candidates.Count == 0)
        {
            return;
        }

        foreach (string userId in candidates)
        {
            // IsLinkedAsync, not ResolveEmployeeIdAsync: since Phase 2 the latter sees active employees
            // only, and a deactivated employee's login is linked, not orphaned (A1).
            if (await hrDirectory.IsLinkedAsync(userId, ct))
            {
                continue;
            }

            if (await clientDirectory.ResolveByAppUserIdAsync(userId, ct) is not null)
            {
                continue;
            }

            Result result = await maintenance.DeleteUnactivatedAsync(userId, ct);
            if (result.IsFailure)
            {
                // Skip, do not fail the sweep: one refused id must not stop the rest being collected.
                logger.LogWarning(
                    "Could not reap unlinked user {UserId}: {ErrorCode}", userId, result.Error.Code);
                continue;
            }

            logger.LogInformation(
                "Reaped unlinked user {UserId}, provisioned more than {RetentionDays} days ago and never "
                + "linked to an employee or a client.",
                userId,
                retentionDays);
        }

        if (candidates.Count == batchSize)
        {
            // Never silently truncate: a full batch means there is more, and the next sweep will take it.
            logger.LogInformation(
                "Reaper processed a full batch of {BatchSize}; more candidates may remain for the next sweep.",
                batchSize);
        }
    }
}
