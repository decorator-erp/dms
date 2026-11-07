using BuildingBlocks.Events;
using Finance.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// F1-08. Opens or re-links the <see cref="Domain.ConditionType.Budget"/> gate a raised variation will
/// eventually unlock — the consumer half of what the backend's <c>VariationDomainService.CreateAsync</c>
/// does by reaching directly into the gate repository.
///
/// <para><b>Re-link before create, matching the backend.</b> An unlinked Budget gate already on the same
/// owner is stamped with this variation id rather than a second gate being opened beside it. Without
/// that, raising two variations against one task would leave the task blocked by two gates and needing
/// both approved — a lock nobody asked for.</para>
///
/// <para><b>A newly created gate LOCKS its owner</b>, which is what an <c>Unlock</c>-trigger gate means
/// (see <c>CreateGateConditionCommandHandler</c>, which does the same on the command path). Re-linking an
/// existing gate does not: the owner is already locked by it.</para>
///
/// <para>Stages the writes only, per <c>InboxDedupeDecorator</c>'s contract — never calls its own
/// <c>SaveChangesAsync</c>.</para>
/// </summary>
internal sealed class VariationRaisedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<VariationRaisedIntegrationEvent>
{
    public async Task HandleAsync(VariationRaisedIntegrationEvent @event, CancellationToken ct)
    {
        // Already linked -- a redelivery. The inbox dedupes, but this keeps the handler correct even if
        // the same variation is announced twice for any other reason.
        if (await db.GateConditions.AnyAsync(g => g.VariationId == @event.VariationId, ct))
        {
            return;
        }

        GateCondition? existing = await db.GateConditions
            .FirstOrDefaultAsync(
                g => g.ConditionType == Domain.ConditionType.Budget
                     && g.VariationId == null
                     && g.Status == Domain.GateStatus.Pending
                     && g.ProjectStageId == @event.ProjectStageId
                     && g.ProjectTaskId == @event.ProjectTaskId,
                ct);

        if (existing is not null)
        {
            existing.LinkVariation(@event.VariationId);
            return;
        }

        Result<GateCondition> created = Domain.GateCondition.Create(
            @event.ProjectStageId, @event.ProjectTaskId,
            Domain.ConditionType.Budget, Domain.GateTrigger.Unlock,
            label: $"Variation {@event.VariationId}",
            variationId: @event.VariationId);

        if (created.IsFailure)
        {
            return;
        }

        db.GateConditions.Add(created.Value!);

        // An Unlock-trigger gate locks its owner the moment it exists.
        if (@event.ProjectTaskId is Guid taskId)
        {
            ProjectTask? task = await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
            task?.Lock();
        }
        else if (@event.ProjectStageId is Guid stageId)
        {
            ProjectStage? stage = await db.ProjectStages.FirstOrDefaultAsync(s => s.Id == stageId, ct);
            stage?.Lock();
        }
    }
}

/// <summary>
/// F1-08. Satisfies the gate carrying the approved variation's id.
///
/// <para><b>The unlock cascade comes for free.</b> <c>MarkSatisfied</c> raises
/// <c>GateConditionSatisfiedDomainEvent</c>, and <c>GateConditionSatisfiedDomainEventHandler</c> is the
/// single place that unlocks the owner and enqueues <c>GateConditionSatisfiedIntegrationEvent</c> —
/// which is exactly why this handler does neither itself. Duplicating the unlock here would run it
/// twice and diverge the moment that cascade changes.</para>
///
/// <para><b><c>ApprovedById</c> is passed through, not fabricated.</b> It is an HR employee id and
/// <c>gate_conditions."ResolvedById"</c> carries a real foreign key to <c>hr.employees</c>, so a made-up
/// Guid would fail the constraint rather than merely mis-attributing the resolution.</para>
/// </summary>
internal sealed class VariationApprovedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<VariationApprovedIntegrationEvent>
{
    public async Task HandleAsync(VariationApprovedIntegrationEvent @event, CancellationToken ct)
    {
        List<GateCondition> gates = await db.GateConditions
            .Where(g => g.VariationId == @event.VariationId && g.Status == Domain.GateStatus.Pending)
            .ToListAsync(ct);

        foreach (GateCondition? gate in gates)
        {
            gate.MarkSatisfied(@event.ApprovedById, "Variation approved.");
        }
    }
}

/// <summary>
/// F1-08. Marks the linked gate Rejected when its variation is rejected or cancelled.
///
/// <para><b>B-P-H4 (owner decision): the owner is released once no Unlock gate of it is still pending</b>, through
/// <see cref="GateOwnerLocks.ReleaseIfLastAsync(ProjectsDbContext, GateCondition, CancellationToken)"/> — the same
/// rule as a gate satisfied, deleted or rejected through Approvals. A rejected gate can never be satisfied again, so
/// leaving the owner Locked (the backend's behaviour) blocked it for good with nothing left to unlock it. The stage
/// is still never sent to Rejected: "this budget change will not happen" is all the rejection means.</para>
///
/// <para>An already-satisfied gate is left alone: the variation was approved, the owner was unlocked,
/// and a later cancellation of the variation record does not retroactively re-block work that has
/// legitimately started.</para>
/// </summary>
internal sealed class VariationClosedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<VariationClosedIntegrationEvent>
{
    public async Task HandleAsync(VariationClosedIntegrationEvent @event, CancellationToken ct)
    {
        List<GateCondition> gates = await db.GateConditions
            .Where(g => g.VariationId == @event.VariationId && g.Status != Domain.GateStatus.Satisfied)
            .ToListAsync(ct);

        string note = @event.Cancelled ? "Variation cancelled." : "Variation rejected.";

        foreach (GateCondition? gate in gates)
        {
            gate.MarkRejected(null, note);
        }

        // Owners in id order, so two handlers releasing overlapping owners take the rows in the same order.
        foreach (GateCondition gate in gates.Where(g => g.Trigger == Domain.GateTrigger.Unlock)
                     .DistinctBy(g => g.ProjectStageId ?? g.ProjectTaskId).OrderBy(g => g.ProjectStageId ?? g.ProjectTaskId))
        {
            await GateOwnerLocks.ReleaseIfLastAsync(db, gate, ct);
        }
    }
}
