using BuildingBlocks.Events;
using Documents.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// Structural item 2's second direction (G7-07), the <see cref="Domain.ConditionType.DocumentApproved"/>
/// half: satisfies every pending gate waiting on the approved lineage
/// (<c>ReferencedDocumentId == event.RootDocumentId</c> — a gate waits on the LINEAGE, not one specific
/// version, per <see cref="DocumentApprovedIntegrationEvent"/>'s own remarks). <c>satisfiedById: null</c>
/// — same system-cascade precedent as <see cref="DocumentUploadedIntegrationEventHandler"/>. Stages the
/// write only, per <c>InboxDedupeDecorator</c>'s contract — never calls its own
/// <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class DocumentApprovedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<DocumentApprovedIntegrationEvent>
{
    public async Task HandleAsync(DocumentApprovedIntegrationEvent @event, CancellationToken ct)
    {
        var gates = await db.GateConditions
            .Where(g =>
                g.ConditionType == Domain.ConditionType.DocumentApproved &&
                g.Status == Domain.GateStatus.Pending &&
                g.ReferencedDocumentId == @event.RootDocumentId)
            .ToListAsync(ct);

        foreach (var gate in gates)
            gate.MarkSatisfied(null);
    }
}
