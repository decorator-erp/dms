using BuildingBlocks.Events;
using Documents.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// Structural item 2's second direction (G7-07): satisfies open <see cref="Domain.ConditionType.Attachment"/>
/// gates when a document lands in their scope. Three match shapes, all pending-only:
/// <list type="bullet">
/// <item>a gate on the task the document was uploaded to (<c>ProjectTaskId == event.ProjectTaskId</c>)</item>
/// <item>a gate on the stage the document was uploaded to directly (<c>ProjectStageId == event.ProjectStageId</c>)</item>
/// <item>the reach-through (correction 8): a gate on the STAGE that OWNS the task the document was
/// uploaded to — resolved locally via <c>ProjectTask.ProjectStageId</c>, no cross-module call needed
/// since this handler already has direct <c>ProjectsDbContext</c> access.</item>
/// </list>
/// <c>satisfiedById: null</c> throughout — this is a system-driven cascade with no employee to attribute
/// it to, the same nullable precedent <see cref="Domain.Gates.GateCondition.MarkSatisfied"/>'s own
/// remarks document for stage/task completion. Stages the write only, per
/// <c>InboxDedupeDecorator</c>'s contract — never calls its own <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class DocumentUploadedIntegrationEventHandler(ProjectsDbContext db)
    : IIntegrationEventHandler<DocumentUploadedIntegrationEvent>
{
    public async Task HandleAsync(DocumentUploadedIntegrationEvent @event, CancellationToken ct)
    {
        Guid? owningStageId = @event.ProjectStageId;

        if (@event.ProjectTaskId is { } taskId)
        {
            owningStageId ??= await db.ProjectTasks.AsNoTracking()
                .Where(t => t.Id == taskId)
                .Select(t => t.ProjectStageId)
                .FirstOrDefaultAsync(ct);
        }

        var gates = await db.GateConditions
            .Where(g => g.ConditionType == Domain.ConditionType.Attachment && g.Status == Domain.GateStatus.Pending)
            .Where(g =>
                (@event.ProjectTaskId != null && g.ProjectTaskId == @event.ProjectTaskId) ||
                (owningStageId != null && g.ProjectStageId == owningStageId))
            .ToListAsync(ct);

        foreach (var gate in gates)
            gate.MarkSatisfied(null);
    }
}
