using Microsoft.EntityFrameworkCore;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence;

/// <summary>
/// P2/NP5 (C1): the ONE place a stage's task counters (<c>TotalTasks</c>, <c>CompletedTasks</c>,
/// <c>ProgressPercent</c>) are written after the stage exists. They are recounted from the task rows in SQL,
/// inside the caller's transaction (required: it throws without one, R1-13), never read-modify-written from a tracked copy:
/// <list type="number">
/// <item>the stage row is taken <c>FOR NO KEY UPDATE</c>, so two transitions in one stage queue here;</item>
/// <item>the recount is a NEW statement, so under READ COMMITTED it sees every task change committed by the
/// transition it queued behind, plus this transaction's own (already saved) change.</item>
/// </list>
/// Two tasks of one stage completed at once therefore both count. A Cancelled task is not part of the total
/// (C2). Call it AFTER the task change is saved: complete, reopen, cancel, reject, manual task create and
/// delete. Project progress is not stored at all; it is computed on read from these counters
/// (<see cref="ProjectProgress"/>).
/// </summary>
internal static class StageCounters
{
    public static async Task RecountAsync(ProjectsDbContext db, Guid? stageId, CancellationToken ct)
    {
        if (stageId is not { } id)
        {
            return;
        }

        // R1-13: outside a transaction the row lock below is released as soon as its statement ends, so two recounts
        // would no longer queue. Every caller runs in the pipeline's (or ProjectWriteLock's) transaction; fail loudly
        // if one ever doesn't.
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("StageCounters.RecountAsync must run inside the caller's transaction.");
        }

        int cancelled = (int)ProjectTaskStatus.Cancelled;
        int completed = (int)ProjectTaskStatus.Completed;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM projects.project_stages WHERE \"Id\" = {id} FOR NO KEY UPDATE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE projects.project_stages AS s
            SET "TotalTasks" = c.total,
                "CompletedTasks" = c.done,
                "ProgressPercent" = CASE WHEN c.total = 0 THEN 0 ELSE round(100.0 * c.done / c.total, 2) END
            FROM (
                SELECT count(*) FILTER (WHERE t."Status" <> {cancelled}) AS total,
                       count(*) FILTER (WHERE t."Status" = {completed}) AS done
                FROM projects.project_tasks AS t
                WHERE t."ProjectStageId" = {id}
            ) AS c
            WHERE s."Id" = {id}
            """, ct);
    }
}
