using BuildingBlocks.Events;
using Crm.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// G9-13's push half of the <see cref="Domain.ConditionType.CrmItemsResolved"/> gate — the one CRM gate
/// condition that supports both scopes (backend's <c>CrmItemsResolvedHandler</c> has no
/// <c>OwnerKind</c> check at all). <see cref="CrmItemsResolvedIntegrationEvent"/> carries both id
/// fields nullable with an implicit XOR (CRM's own correction, recorded in its own remarks): exactly
/// one of <c>ProjectStageId</c>/<c>ProjectTaskId</c> is set per publish, so only the matching branch's
/// gates are ever queried. <c>satisfiedById: null</c> — same system-cascade precedent as
/// <c>DocumentUploadedIntegrationEventHandler</c>. Stages the write only, per
/// <c>InboxDedupeDecorator</c>'s contract — never calls its own <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class CrmItemsResolvedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<CrmItemsResolvedIntegrationEvent>
{
    public async Task HandleAsync(CrmItemsResolvedIntegrationEvent @event, CancellationToken ct)
    {
        List<GateCondition> gates = await db.GateConditions
            .Where(g => g.ConditionType == Domain.ConditionType.CrmItemsResolved && g.Status == Domain.GateStatus.Pending)
            .Where(g =>
                @event.ProjectStageId != null && g.ProjectStageId == @event.ProjectStageId ||
                @event.ProjectTaskId != null && g.ProjectTaskId == @event.ProjectTaskId)
            .ToListAsync(ct);

        foreach (GateCondition? gate in gates)
        {
            gate.MarkSatisfied(null);
        }
    }
}
