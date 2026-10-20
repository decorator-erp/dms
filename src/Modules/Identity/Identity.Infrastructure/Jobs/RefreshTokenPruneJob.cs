using BuildingBlocks.Jobs;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Jobs;

/// <summary>
/// Aug 4.1: deletes refresh-token rows nothing can use any more, so the collection every user load
/// brings back stays bounded. A row goes when it expired more than <c>PruneGraceDays</c> ago, or was
/// revoked more than <c>RevokedRetentionHours</c> ago (see <see cref="RefreshTokenOptions"/> for what
/// that window costs).
///
/// <para><b>Safe beside live requests</b> because of the invariant on <c>ApplicationUser</c>'s token
/// methods: requests only modify active tokens, and every row deleted here is expired or long revoked.</para>
///
/// <para><b>Plain SQL, in batches.</b> The token is an owned type, which can't be the root of an
/// <c>ExecuteDeleteAsync</c>, so each batch is one <c>DELETE … WHERE id IN (SELECT … LIMIT n)</c> on this
/// module's own table. Housekeeping on unusable rows, so it is deliberately not audited. A sweep runs
/// batches until one comes back short, at most <c>PruneMaxBatchesPerSweep</c>, so a large backlog is
/// worked off over several sweeps instead of in one long statement.</para>
/// </summary>
internal sealed class RefreshTokenPruneJob(
    IdentityModuleDbContext db,
    IOptionsSnapshot<RefreshTokenOptions> options, // read per run (the job is scoped), like the reaper's settings
    TimeProvider timeProvider,
    ILogger<RefreshTokenPruneJob> logger) : IPeriodicJob
{
    public string Name => nameof(RefreshTokenPruneJob);

    public async Task RunAsync(CancellationToken ct)
    {
        RefreshTokenOptions o = options.Value;
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime expiredBefore = now.AddDays(-o.PruneGraceDays);
        DateTime revokedBefore = now.AddHours(-o.RevokedRetentionHours);
        int batchSize = Math.Max(1, o.PruneBatchSize);

        int total = 0;
        for (int batch = 0; batch < Math.Max(1, o.PruneMaxBatchesPerSweep); batch++)
        {
            int deleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM identity.refresh_tokens
                WHERE id IN (
                    SELECT id FROM identity.refresh_tokens
                    WHERE "ExpiresOn" < {expiredBefore} OR "RevokedOn" < {revokedBefore}
                    LIMIT {batchSize})
                """, ct);
            total += deleted;

            if (deleted < batchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            logger.LogInformation("Pruned {Count} unusable refresh tokens.", total);
        }
    }
}
