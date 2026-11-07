using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Gates;

/// <summary>
/// Stage/task completion triggers re-evaluation of any PENDING gate that references the just-completed
/// owner — "resolved by" is the same employee who completed the referenced stage (its own
/// <c>CompletedById</c>), since no human explicitly decided the DEPENDENT gate itself. Runs inside the
/// SAME command-path dispatch loop that raised <see cref="Domain.ProjectStageCompletedDomainEvent"/>
/// (the G5-08 command-path transaction fix — see <c>DomainEventDispatchDecorator</c>'s own remarks — is
/// what makes staging <see cref="Domain.GateCondition.MarkSatisfied"/> here, and its own cascading
/// domain event, commit atomically with the triggering completion).
/// </summary>
internal sealed class StageCompletedGateResolutionHandler(ProjectsDbContext db)
    : IDomainEventHandler<Domain.ProjectStageCompletedDomainEvent>
{
    public async Task Handle(Domain.ProjectStageCompletedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Guid? completedById = await db.ProjectStages.AsNoTracking()
            .Where(s => s.Id == domainEvent.StageId)
            .Select(s => s.CompletedById)
            .FirstOrDefaultAsync(cancellationToken);

        List<GateCondition> gates = await db.GateConditions
            .Where(g => g.ConditionType == Domain.ConditionType.Stage
                && g.ReferencedStageId == domainEvent.StageId
                && g.Status == Domain.GateStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (GateCondition? gate in gates)
        {
            gate.MarkSatisfied(completedById);
        }
    }
}

/// <summary>Identical shape to <see cref="StageCompletedGateResolutionHandler"/>, for gates that
/// reference a just-completed TASK.</summary>
internal sealed class TaskCompletedGateResolutionHandler(ProjectsDbContext db)
    : IDomainEventHandler<Domain.ProjectTaskCompletedDomainEvent>
{
    public async Task Handle(Domain.ProjectTaskCompletedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Guid? completedById = domainEvent.AssigneeEmployeeId;

        List<GateCondition> gates = await db.GateConditions
            .Where(g => g.ConditionType == Domain.ConditionType.Task
                && g.ReferencedTaskId == domainEvent.TaskId
                && g.Status == Domain.GateStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (GateCondition? gate in gates)
        {
            gate.MarkSatisfied(completedById);
        }
    }
}

/// <summary>
/// The single place that reacts to ANY gate becoming satisfied, regardless of which of the three paths
/// caused it (manual <c>SatisfyGateConditionCommand</c>, the stage/task completion cascade above, or
/// <c>ApprovalDecidedIntegrationEventHandler</c>): releases the owner of an <see cref="Domain.GateTrigger.Unlock"/>
/// gate once no other Unlock gate of it is pending (NP1, <see cref="GateOwnerLocks.ReleaseIfLastAsync(ProjectsDbContext, Domain.GateOwnerKind, Guid, CancellationToken)"/>),
/// and enqueues <see cref="GateConditionSatisfiedIntegrationEvent"/> either way. Kept as ONE
/// domain-event handler reacting to <see cref="Domain.GateConditionSatisfiedDomainEvent"/> rather than
/// duplicating "unlock + enqueue" at each of the three call sites — the entity already raises this exact
/// event for exactly this purpose, and the dispatch loop's own transaction (F1 for the integration-event
/// path, this task's own fix for the command path) is what makes staging the integration event here,
/// rather than at the original call site, safe and atomic.
/// </summary>
internal sealed class GateConditionSatisfiedDomainEventHandler(ProjectsDbContext db)
    : IDomainEventHandler<Domain.GateConditionSatisfiedDomainEvent>
{
    public async Task Handle(Domain.GateConditionSatisfiedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Domain.GateTrigger? trigger = await db.GateConditions.AsNoTracking()
            .Where(g => g.Id == domainEvent.GateConditionId)
            .Select(g => (Domain.GateTrigger?)g.Trigger)
            .FirstOrDefaultAsync(cancellationToken);

        Guid projectId = domainEvent.OwnerKind == Domain.GateOwnerKind.Stage
            ? await db.ProjectStages.AsNoTracking().Where(s => s.Id == domainEvent.OwnerId).Select(s => s.ProjectId).FirstOrDefaultAsync(cancellationToken)
            : await db.ProjectTasks.AsNoTracking().Where(t => t.Id == domainEvent.OwnerId).Select(t => t.ProjectId).FirstOrDefaultAsync(cancellationToken);

        // NP1: the owner opens only when this was its last pending Unlock gate (GateOwnerLocks).
        if (trigger == Domain.GateTrigger.Unlock)
        {
            await GateOwnerLocks.ReleaseIfLastAsync(db, domainEvent.OwnerKind, domainEvent.OwnerId, cancellationToken);
        }

        db.Outbox().Enqueue(new GateConditionSatisfiedIntegrationEvent(
            Guid.NewGuid(), projectId, domainEvent.GateConditionId,
            ProjectsEnumMapping.ToContracts(domainEvent.OwnerKind), domainEvent.OwnerId, DateTimeOffset.UtcNow));
    }
}
