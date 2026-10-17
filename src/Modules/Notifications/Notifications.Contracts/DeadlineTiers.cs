namespace Notifications.Contracts;

/// <summary>
/// Shared tier logic for deadline reminders. A tier is a day-offset relative to the due date (negative
/// = before due, positive = overdue). A tier is "crossed" when <c>now >= due + tier days</c>; only the
/// single most relevant (largest crossed) tier is emitted per run — earlier tiers that were never
/// emitted are skipped, and the dedupe key makes each tier fire at most once ever.
///
/// <para><b>Here rather than in BuildingBlocks</b>, which is where PLAN-PHASE6.md §3 put it.
/// <see cref="Phrase"/> returns user-facing notification body text ("due within 3 day(s)") — that is
/// Notifications vocabulary, not scheduling infrastructure. And it costs nothing: every consumer
/// (Projects, Crm and Approvals' <c>.Application</c>) already references this project for
/// <see cref="INotificationService"/>. <c>IPeriodicJob</c> and <c>PeriodicJobWorker</c> stay in
/// BuildingBlocks, which is what keeps BuildingBlocks free of any dependency on Notifications.</para>
/// </summary>
public static class DeadlineTiers
{
    /// <summary>
    /// The largest crossed tier, or null when none has been crossed. Emitting only the largest is what
    /// makes a missed sweep harmless: a job that was down for two days emits the −1d tier's successor
    /// rather than replaying every tier it slept through.
    /// </summary>
    public static int? MostRelevant(DateTime nowUtc, DateTime dueUtc, ReadOnlySpan<int> tiers)
    {
        int? best = null;
        foreach (int tier in tiers)
        {
            if (nowUtc >= dueUtc.AddDays(tier) && (best is null || tier > best))
            {
                best = tier;
            }
        }

        return best;
    }

    /// <summary>"due within N day(s)" / "due today" / "overdue by N+ day(s)" phrasing for a crossed tier.</summary>
    public static string Phrase(int tier) => tier switch
    {
        < 0 => $"due within {-tier} day(s)",
        0 => "due today",
        > 0 => $"overdue by {tier}+ day(s)",
    };
}
