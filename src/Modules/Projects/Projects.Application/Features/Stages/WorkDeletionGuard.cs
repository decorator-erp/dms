using Crm.Contracts;
using Documents.Contracts;
using Finance.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Infrastructure.Persistence;
using Sites.Contracts;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Stages;

/// <summary>
/// crm-complete E2/E3 (D2, "delete is allowed only before the item has started"): decides whether a set of
/// tasks — a stage's, or one task — can be hard-deleted (a stage only while Active or Locked, FINDING-E-4). Inside Projects: every task must still be Open or
/// Locked, with no done sub-task, no comment and no satisfied gate; and no gate elsewhere may reference the
/// stage or the tasks, and no pending approval gate. Outside it, through contracts only: no document row or folder,
/// active or not (Documents), no brief, period, item or
/// brief-space entry (CRM) and no budget, ledger, invoice, variation or BOQ row (Finance) may point at them.
/// A fixed number of statements whatever the task count; the caller holds the project lock
/// (<see cref="ProjectWriteLock"/>), so the answer still holds when it deletes.
///
/// <para>P1: <see cref="CanDeleteProjectAsync"/> applies the same rules to a whole project at once (set-based,
/// so references between the project's own stages and tasks don't block), plus the project-level ones:
/// no document or folder, no site (soft-deleted ones included, <see cref="ISiteProjectUsage"/>), and no CRM or Finance
/// row naming the project.</para>
/// </summary>
internal sealed class WorkDeletionGuard(
    ProjectsDbContext db, IDocumentQueries documents, ICrmProjectWorkUsage crm, IFinanceProjectWorkUsage finance)
{
    public async Task<bool> CanDeleteStageAsync(Guid stageId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        if (!await CanDeleteTasksAsync(taskIds, ct))
        {
            return false;
        }

        // FINDING-E-8: a pending approval gate has an open request in the Approvals inbox; deleting the gate
        // would orphan it.
        bool gateBlocks =
            await db.GateConditions.AsNoTracking().AnyAsync(g =>
                g.ProjectStageId == stageId && g.ConditionType == Domain.ConditionType.Approval && g.Status == Domain.GateStatus.Pending, ct)
            || await db.GateConditions.AsNoTracking().AnyAsync(g => g.ProjectStageId == stageId && g.Status == Domain.GateStatus.Satisfied, ct)
            || await db.GateConditions.AsNoTracking().AnyAsync(g => g.ReferencedStageId == stageId && g.ProjectStageId != stageId, ct);
        if (gateBlocks)
        {
            return false;
        }

        Guid[] ids = [stageId];
        return (await documents.StagesReferencedAsync(ids, ct)).Count == 0
            && (await crm.StagesInUseAsync(ids, ct)).Count == 0
            && (await finance.StagesInUseAsync(ids, ct)).Count == 0;
    }

    public Task<bool> CanDeleteTaskAsync(Guid taskId, CancellationToken ct) => CanDeleteTasksAsync([taskId], ct);

    /// <summary>P1: every stage still Active or Locked and every task never started and untouched (the same rules
    /// as <see cref="CanDeleteStageAsync"/>/<see cref="CanDeleteTaskAsync"/>, for the whole set at once); no
    /// satisfied or pending-approval gate on the work; no gate outside the project referencing it; and nothing in
    /// Documents, Sites, CRM or Finance naming the project, its stages or its tasks. The caller holds the project
    /// lock and has taken the work rows with <see cref="LockProjectWorkAsync"/>.</summary>
    public async Task<bool> CanDeleteProjectAsync(
        Guid projectId, IReadOnlyCollection<Guid> stageIds, IReadOnlyCollection<Guid> taskIds, ISiteProjectUsage sites,
        CancellationToken ct)
    {
        bool closedStage = await db.ProjectStages.AsNoTracking().AnyAsync(s =>
            s.ProjectId == projectId && s.Status != Domain.StageStatus.Active && s.Status != Domain.StageStatus.Locked, ct);
        bool startedTask = await db.ProjectTasks.AsNoTracking().AnyAsync(t =>
            t.ProjectId == projectId && t.Status != Domain.ProjectTaskStatus.Open && t.Status != Domain.ProjectTaskStatus.Locked, ct);
        if (closedStage || startedTask)
        {
            return false;
        }

        bool touched = await db.ProjectSubTasks.AsNoTracking().AnyAsync(s => taskIds.Contains(s.ProjectTaskId) && s.IsDone, ct)
            || await db.TaskComments.AsNoTracking().AnyAsync(c => taskIds.Contains(c.TaskId), ct)
            || await db.GateConditions.AsNoTracking().AnyAsync(g =>
                (g.ProjectStageId != null && stageIds.Contains(g.ProjectStageId.Value) || g.ProjectTaskId != null && taskIds.Contains(g.ProjectTaskId.Value))
                && (g.Status == Domain.GateStatus.Satisfied
                    || g.ConditionType == Domain.ConditionType.Approval && g.Status == Domain.GateStatus.Pending), ct)
            || await db.GateConditions.AsNoTracking().AnyAsync(g =>
                (g.ReferencedStageId != null && stageIds.Contains(g.ReferencedStageId.Value)
                    || g.ReferencedTaskId != null && taskIds.Contains(g.ReferencedTaskId.Value))
                && (g.ProjectStageId == null || !stageIds.Contains(g.ProjectStageId.Value))
                && (g.ProjectTaskId == null || !taskIds.Contains(g.ProjectTaskId.Value)), ct);
        if (touched)
        {
            return false;
        }

        Guid[] project = [projectId];
        return (await documents.ProjectsReferencedAsync(project, ct)).Count == 0
            && (await documents.StagesReferencedAsync(stageIds, ct)).Count == 0
            && (await documents.TasksReferencedAsync(taskIds, ct)).Count == 0
            && (await sites.ProjectsInUseAsync(project, ct)).Count == 0
            && (await crm.ProjectsInUseAsync(project, ct)).Count == 0
            && (await crm.StagesInUseAsync(stageIds, ct)).Count == 0
            && (await crm.TasksInUseAsync(taskIds, ct)).Count == 0
            && (await finance.ProjectsInUseAsync(project, ct)).Count == 0
            && (await finance.StagesInUseAsync(stageIds, ct)).Count == 0
            && (await finance.TasksInUseAsync(taskIds, ct)).Count == 0;
    }

    /// <summary>P1: takes every stage and task row of the project <c>FOR UPDATE</c> (one statement each) before the
    /// checks, as <see cref="LockAsync"/> does for one stage, so a racing transition, comment or sub-task completion
    /// either commits first and is seen, or waits and then finds its row gone.</summary>
    public async Task LockProjectWorkAsync(Guid projectId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM projects.project_stages WHERE \"ProjectId\" = {projectId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM projects.project_tasks WHERE \"ProjectId\" = {projectId} FOR UPDATE", ct);
    }

    /// <summary>FINDING-E-3: locks the rows about to be deleted (<c>FOR UPDATE</c>) before any check, so a writer
    /// that takes the task row (<see cref="ProjectWriteLock.ShareTaskAsync"/>: comment, sub-task complete, start)
    /// either commits first and is seen by the check, or waits and then finds the row gone.</summary>
    public async Task LockAsync(Guid? stageId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        if (stageId is { } id)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM projects.project_stages WHERE \"Id\" = {id} FOR UPDATE", ct);
        }

        foreach (Guid taskId in taskIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM projects.project_tasks WHERE \"Id\" = {taskId} FOR UPDATE", ct);
        }
    }

    private async Task<bool> CanDeleteTasksAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return true;
        }

        bool started = await db.ProjectTasks.AsNoTracking().AnyAsync(t =>
            taskIds.Contains(t.Id)
            && t.Status != Domain.ProjectTaskStatus.Open && t.Status != Domain.ProjectTaskStatus.Locked, ct);
        if (started)
        {
            return false;
        }

        bool touched = await db.ProjectSubTasks.AsNoTracking().AnyAsync(s => taskIds.Contains(s.ProjectTaskId) && s.IsDone, ct)
            || await db.TaskComments.AsNoTracking().AnyAsync(c => taskIds.Contains(c.TaskId), ct)
            || await db.GateConditions.AsNoTracking().AnyAsync(g =>
                g.ProjectTaskId != null && taskIds.Contains(g.ProjectTaskId.Value) && g.Status == Domain.GateStatus.Satisfied, ct)
            || await db.GateConditions.AsNoTracking().AnyAsync(g =>
                g.ProjectTaskId != null && taskIds.Contains(g.ProjectTaskId.Value)
                && g.ConditionType == Domain.ConditionType.Approval && g.Status == Domain.GateStatus.Pending, ct)
            || await db.GateConditions.AsNoTracking().AnyAsync(g =>
                g.ReferencedTaskId != null && taskIds.Contains(g.ReferencedTaskId.Value)
                && (g.ProjectTaskId == null || !taskIds.Contains(g.ProjectTaskId.Value)), ct);
        if (touched)
        {
            return false;
        }

        return (await documents.TasksReferencedAsync(taskIds, ct)).Count == 0
            && (await crm.TasksInUseAsync(taskIds, ct)).Count == 0
            && (await finance.TasksInUseAsync(taskIds, ct)).Count == 0;
    }

    /// <summary>Removes the tasks with their sub-tasks and pending gates (comments can't exist: the guard
    /// refused them).</summary>
    public async Task RemoveTasksAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return;
        }

        db.ProjectSubTasks.RemoveRange(await db.ProjectSubTasks.Where(s => taskIds.Contains(s.ProjectTaskId)).ToListAsync(ct));
        db.GateConditions.RemoveRange(await db.GateConditions
            .Where(g => g.ProjectTaskId != null && taskIds.Contains(g.ProjectTaskId.Value)).ToListAsync(ct));
        db.ProjectTasks.RemoveRange(await db.ProjectTasks.Where(t => taskIds.Contains(t.Id)).ToListAsync(ct));
    }
}
