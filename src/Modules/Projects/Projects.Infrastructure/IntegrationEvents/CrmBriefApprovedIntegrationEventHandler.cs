using BuildingBlocks.Events;
using Crm.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// G9-13's push half of the <see cref="Domain.ConditionType.CrmBriefApproved"/> gate: satisfies every
/// pending gate on the stage a CRM design brief was approved for. <see cref="CrmBriefApprovedIntegrationEvent"/>
/// carries a required <c>ProjectStageId</c> — a design brief is always stage-owned (Lesson 1: the CRM
/// gate handlers only ever check <c>OwnerKind</c>, and only stage-owned gates exist for this condition
/// type), so no task-scoped match branch applies here, unlike
/// <see cref="CrmItemsResolvedIntegrationEventHandler"/>. <c>satisfiedById: null</c> — same
/// system-cascade precedent as <c>DocumentUploadedIntegrationEventHandler</c>. Stages the write only,
/// per <c>InboxDedupeDecorator</c>'s contract — never calls its own <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class CrmBriefApprovedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<CrmBriefApprovedIntegrationEvent>
{
    public async Task HandleAsync(CrmBriefApprovedIntegrationEvent @event, CancellationToken ct)
    {
        List<GateCondition> gates = await db.GateConditions
            .Where(g =>
                g.ConditionType == Domain.ConditionType.CrmBriefApproved &&
                g.Status == Domain.GateStatus.Pending &&
                g.ProjectStageId == @event.ProjectStageId)
            .ToListAsync(ct);

        foreach (GateCondition? gate in gates)
        {
            gate.MarkSatisfied(null);
        }
    }
}
