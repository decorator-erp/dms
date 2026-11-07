using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence;

/// <summary>
/// P4/NP1/B-P-H4: the ONE place an <see cref="GateTrigger.Unlock"/> gate's owner (a stage or a task) is released.
/// An owner is unlocked only when none of its Unlock gates is still Pending — so with two Unlock gates, the first
/// satisfied leaves it Locked (NP1), and an owner whose last pending Unlock gate is deleted (P4) or rejected (an
/// approval decision, a Finance variation rejected or cancelled: B-P-H4) is released instead of staying Locked for
/// good. Called by gate satisfaction (<c>GateConditionSatisfiedDomainEventHandler</c>, which every satisfy path —
/// manual, completion cascade, approval, Finance, CRM, Documents — goes through), gate delete, approval rejection and
/// variation reject/cancel.
///
/// <para><b>The owner row is locked first</b> (<c>FOR NO KEY UPDATE</c>), so two gates of one owner resolved at once
/// queue here: the second re-reads after the first commits and sees it. The pending check merges the database with
/// this context's tracked gates (a gate changed or deleted but not yet saved counts as it is now), because the
/// integration-event and cascade paths call this before the change is flushed.</para>
///
/// <para><b>Lock order (R1-2): stage rows before task rows, each set sorted by id.</b> A transition that both
/// recounts its own stage (<see cref="StageCounters"/>) and cascades into other owners' Unlock gates takes every one
/// of those stage rows up front in one statement, then every task row (its own and the cascade's) in another
/// (<see cref="LockCompletionCascadeAsync"/>), so two such transitions crossing each other's rows, or a transition
/// crossing a stage delete, queue instead of deadlocking (A then B against B then A).</para>
/// </summary>
internal static class GateOwnerLocks
{
    /// <summary>Releases the owner of <paramref name="gate"/> when it is an Unlock gate and no other Unlock gate of
    /// that owner is still pending. A Complete-trigger gate never locked anything, so it is a no-op.</summary>
    public static Task ReleaseIfLastAsync(ProjectsDbContext db, GateCondition gate, CancellationToken ct) =>
        gate.Trigger == GateTrigger.Unlock
            ? ReleaseIfLastAsync(db, gate.Scope, gate.ProjectStageId ?? gate.ProjectTaskId!.Value, ct)
            : Task.CompletedTask;

    public static async Task ReleaseIfLastAsync(ProjectsDbContext db, GateOwnerKind kind, Guid ownerId, CancellationToken ct)
    {
        if (kind == GateOwnerKind.Stage)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM projects.project_stages WHERE \"Id\" = {ownerId} FOR NO KEY UPDATE", ct);
            if (!await AnyPendingUnlockAsync(db, kind, ownerId, ct))
            {
                ProjectStage? stage = await db.ProjectStages.FirstOrDefaultAsync(s => s.Id == ownerId, ct);
                stage?.Unlock();
            }

            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM projects.project_tasks WHERE \"Id\" = {ownerId} FOR NO KEY UPDATE", ct);
        if (!await AnyPendingUnlockAsync(db, kind, ownerId, ct))
        {
            ProjectTask? task = await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == ownerId, ct);
            task?.Unlock();
        }
    }

    /// <summary>Takes, sorted by id in one statement, the completing owner's own stage (<paramref name="ownStageId"/>,
    /// which <see cref="StageCounters"/> will recount) plus every stage whose pending Unlock gate the completion of
    /// <paramref name="completedTaskId"/> or <paramref name="completedStageId"/> is about to satisfy; then (R1-2: stage
    /// rows before task rows), sorted by id in one statement, the completing task itself and every task whose pending
    /// Unlock gate the completion satisfies, so the cascade's <see cref="ReleaseIfLastAsync(ProjectsDbContext, GateOwnerKind, Guid, CancellationToken)"/>
    /// finds them already held. Call it before taking any task row.</summary>
    public static async Task LockCompletionCascadeAsync(
        ProjectsDbContext db, Guid? ownStageId, Guid? completedTaskId, Guid? completedStageId, CancellationToken ct)
    {
        var owners = await db.GateConditions.AsNoTracking()
            .Where(g => g.Trigger == GateTrigger.Unlock && g.Status == GateStatus.Pending
                && (completedTaskId != null && g.ConditionType == ConditionType.Task && g.ReferencedTaskId == completedTaskId
                    || completedStageId != null && g.ConditionType == ConditionType.Stage && g.ReferencedStageId == completedStageId))
            .Select(g => new { g.ProjectStageId, g.ProjectTaskId })
            .ToListAsync(ct);

        List<Guid> stageIds = [.. owners.Where(o => o.ProjectStageId != null).Select(o => o.ProjectStageId!.Value)];
        if (ownStageId is { } own)
        {
            stageIds.Add(own);
        }

        List<Guid> taskIds = [.. owners.Where(o => o.ProjectTaskId != null).Select(o => o.ProjectTaskId!.Value)];
        if (completedTaskId is { } completed)
        {
            taskIds.Add(completed);
        }

        Guid[] stages = [.. stageIds.Distinct().Order()];
        if (stages.Length > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM projects.project_stages WHERE \"Id\" = ANY({stages}) ORDER BY \"Id\" FOR NO KEY UPDATE", ct);
        }

        Guid[] tasks = [.. taskIds.Distinct().Order()];
        if (tasks.Length > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM projects.project_tasks WHERE \"Id\" = ANY({tasks}) ORDER BY \"Id\" FOR NO KEY UPDATE", ct);
        }
    }

    private static async Task<bool> AnyPendingUnlockAsync(ProjectsDbContext db, GateOwnerKind kind, Guid ownerId, CancellationToken ct)
    {
        IQueryable<GateCondition> owned = kind == GateOwnerKind.Stage
            ? db.GateConditions.AsNoTracking().Where(g => g.ProjectStageId == ownerId)
            : db.GateConditions.AsNoTracking().Where(g => g.ProjectTaskId == ownerId);
        var stored = await owned.Where(g => g.Trigger == GateTrigger.Unlock)
            .Select(g => new { g.Id, g.Status })
            .ToListAsync(ct);

        var tracked = db.ChangeTracker.Entries<GateCondition>()
            .Where(e => e.Entity.Trigger == GateTrigger.Unlock
                && (kind == GateOwnerKind.Stage ? e.Entity.ProjectStageId == ownerId : e.Entity.ProjectTaskId == ownerId))
            .ToDictionary(e => e.Entity.Id);

        bool storedPending = stored.Any(row => tracked.TryGetValue(row.Id, out EntityEntry<GateCondition>? entry)
            ? entry.State != EntityState.Deleted && entry.Entity.IsPending
            : row.Status == GateStatus.Pending);
        return storedPending
            || tracked.Values.Any(e => e.State == EntityState.Added && e.Entity.IsPending);
    }
}
