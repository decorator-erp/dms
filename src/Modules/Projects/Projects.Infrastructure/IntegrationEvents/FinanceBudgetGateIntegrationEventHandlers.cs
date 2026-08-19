using BuildingBlocks.Events;
using Finance.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// F1-08. Acts on a task's budget-coverage verdict — <b>in both directions</b>.
///
/// <para><b>The false branch is half the point, not an edge case.</b> The backend's
/// <c>ResolveTaskBudgetGatesAsync</c> satisfies pending Budget gates when a task becomes covered AND
/// re-pends satisfied ones, pushing the task back to Locked, when coverage is lost. A consumer handling
/// only the true branch leaves a gate satisfied on a task that has gone over budget — work continues
/// against money that is not there, which is the exact failure the gate exists to prevent.</para>
///
/// <para><b>Re-locking is conditional on the task still having an unsatisfied Unlock gate</b>, matching
/// the backend's own predicate. Without that check a task whose other gates are all satisfied would be
/// locked by this one and then have nothing left to unlock it.</para>
///
/// <para>Satisfying goes through <c>MarkSatisfied</c>, so the ordinary unlock cascade
/// (<c>GateConditionSatisfiedDomainEventHandler</c>) runs — this handler never unlocks directly.
/// <c>satisfiedById: null</c> is the system-cascade precedent <c>DocumentUploadedIntegrationEventHandler</c>
/// set: no human resolved it, and <c>ResolvedById</c> has a real FK to <c>hr.employees</c>.</para>
/// </summary>
internal sealed class TaskBudgetCoverageChangedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<TaskBudgetCoverageChangedIntegrationEvent>
{
    public async Task HandleAsync(TaskBudgetCoverageChangedIntegrationEvent @event, CancellationToken ct)
    {
        if (@event.IsCovered)
        {
            var pending = await db.GateConditions
                .Where(g => g.ProjectTaskId == @event.ProjectTaskId
                            && g.ConditionType == Domain.ConditionType.Budget
                            && g.Status == Domain.GateStatus.Pending)
                .ToListAsync(ct);

            foreach (var gate in pending)
                gate.MarkSatisfied(null, "Task budget covered.");

            return;
        }

        var satisfied = await db.GateConditions
            .Where(g => g.ProjectTaskId == @event.ProjectTaskId
                        && g.ConditionType == Domain.ConditionType.Budget
                        && g.Status == Domain.GateStatus.Satisfied)
            .ToListAsync(ct);

        if (satisfied.Count == 0)
            return;

        foreach (var gate in satisfied)
            gate.MarkPending("Task is over budget without a signed difference.");

        await RelockTaskAsync(db, @event.ProjectTaskId, satisfied, ct);
    }

    /// <summary>
    /// Locks the task if it is Open and one of the gates just re-pended is an <c>Unlock</c>-trigger gate.
    ///
    /// <para><b>The blocked check reads the in-memory list, NOT the database, and that is the whole
    /// point.</b> The first version of this queried <c>db.GateConditions</c> for an unsatisfied Unlock
    /// gate — after <c>MarkPending</c> had mutated the tracked entities but before
    /// <c>InboxDedupeDecorator</c> saves them. The query therefore saw the rows still marked
    /// <c>Satisfied</c>, found nothing blocking, and silently skipped the lock: the gate re-pended and
    /// the task stayed Open, which is the exact failure the de-satisfy path exists to prevent. Caught by
    /// <c>GoingOverBudget_RePendsASatisfiedGateAndRelocksTheTask</c>.</para>
    ///
    /// <para>Reading the list is also equivalent to the backend's predicate rather than merely close to
    /// it: a gate this handler just re-pended IS an unsatisfied Unlock gate on this task, so
    /// <c>Any(unlock &amp;&amp; != Satisfied)</c> is true by construction whenever the list is non-empty.</para>
    /// </summary>
    private static async Task RelockTaskAsync(
        ProjectsDbContext db, Guid taskId, List<Domain.GateCondition> rePended, CancellationToken ct)
    {
        if (!rePended.Any(g => g.Trigger == Domain.GateTrigger.Unlock))
            return;

        var task = await db.ProjectTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.IsActive, ct);

        if (task is not null && task.Status == Domain.ProjectTaskStatus.Open)
            task.Lock();
    }
}

/// <summary>
/// F1-08. The stage counterpart, on the same two-directional contract.
///
/// <para>Funding is <c>ClientInvoicesSum &gt; 0 &amp;&amp; ClientPaymentsSum &gt;= ClientInvoicesSum</c>,
/// decided in Finance (<c>FinanceGateVerdicts.IsStageFundedAsync</c>) and arriving here as a bool. The
/// <c>&gt; 0</c> half matters at this end too: without it an uninvoiced stage would arrive as funded and
/// this handler would dutifully unlock it.</para>
/// </summary>
internal sealed class StageFundingChangedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<StageFundingChangedIntegrationEvent>
{
    public async Task HandleAsync(StageFundingChangedIntegrationEvent @event, CancellationToken ct)
    {
        if (@event.IsFunded)
        {
            var pending = await db.GateConditions
                .Where(g => g.ProjectStageId == @event.ProjectStageId
                            && g.ConditionType == Domain.ConditionType.Budget
                            && g.Status == Domain.GateStatus.Pending)
                .ToListAsync(ct);

            foreach (var gate in pending)
                gate.MarkSatisfied(null, "Stage funding reached.");

            return;
        }

        var satisfied = await db.GateConditions
            .Where(g => g.ProjectStageId == @event.ProjectStageId
                        && g.ConditionType == Domain.ConditionType.Budget
                        && g.Status == Domain.GateStatus.Satisfied)
            .ToListAsync(ct);

        if (satisfied.Count == 0)
            return;

        foreach (var gate in satisfied)
            gate.MarkPending("Stage funding no longer met.");

        // In-memory, not a re-query -- see RelockTaskAsync above for the stale-read this avoids.
        if (!satisfied.Any(g => g.Trigger == Domain.GateTrigger.Unlock))
            return;

        var stage = await db.ProjectStages
            .FirstOrDefaultAsync(s => s.Id == @event.ProjectStageId && s.IsActive, ct);

        if (stage is not null && stage.Status == Domain.StageStatus.Active)
            stage.Lock();
    }
}
