using BuildingBlocks.Events;
using Crm.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// G9-13's push half of the <see cref="Domain.ConditionType.CrmPeriodClosed"/> gate: satisfies every
/// pending gate on the stage a CRM requirement period was closed for. Stage-owned only, same reasoning
/// as <see cref="CrmBriefApprovedIntegrationEventHandler"/> — a requirement period is stage-owned, and
/// Lesson 1 confirmed the backend's own handler only checks <c>OwnerKind</c>. <c>satisfiedById: null</c>
/// — same system-cascade precedent as <c>DocumentUploadedIntegrationEventHandler</c>. Stages the write
/// only, per <c>InboxDedupeDecorator</c>'s contract — never calls its own <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class CrmPeriodClosedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<CrmPeriodClosedIntegrationEvent>
{
    public async Task HandleAsync(CrmPeriodClosedIntegrationEvent @event, CancellationToken ct)
    {
        List<GateCondition> gates = await db.GateConditions
            .Where(g =>
                g.ConditionType == Domain.ConditionType.CrmPeriodClosed &&
                g.Status == Domain.GateStatus.Pending &&
                g.ProjectStageId == @event.ProjectStageId)
            .ToListAsync(ct);

        foreach (GateCondition? gate in gates)
        {
            gate.MarkSatisfied(null);
        }
    }
}
