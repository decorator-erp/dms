using BuildingBlocks.Events;
using Documents.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// Structural item 2's second direction (G7-07): satisfies open <see cref="Domain.ConditionType.Attachment"/>
/// gates when a document lands in their scope. Two match shapes, both pending-only:
/// <list type="bullet">
/// <item>a gate on the task the document was uploaded to (<c>ProjectTaskId == event.ProjectTaskId</c>)</item>
/// <item>a gate on the document's stage — which, since E1-01, is the owning stage of its task for a
/// task-scoped upload, denormalized at write time onto <c>ProjectDocument.ProjectStageId</c> and carried
/// on the event.</item>
/// </list>
///
/// <para><b>The <c>owningStageId ??=</c> lookup below is deliberately kept, and is NOT dead code — do not
/// "simplify" it away.</b> Before E1-01 a task upload published <c>ProjectStageId = null</c> and this
/// lookup was the ONLY thing satisfying a stage gate (correction 8's gate half). E1-01 denormalized the
/// stage at upload, so for every current producer the event now arrives with it and this fallback no
/// longer changes an outcome. It remains as defense in depth for two real cases: rows written before
/// E1-01 (never backfilled — see that task's <c>PROGRESS.md</c> entry), and any future producer that
/// publishes this event without denormalizing. Deleting it would make gate correctness depend on every
/// writer, forever, remembering to do so — which is precisely how E1 happened. Its coverage is
/// <c>FileFlowTests.AttachmentGate_OnTheOwningStage_IsSatisfied_ByAnEventCarryingNoStage</c>, which
/// publishes a synthetic null-stage event specifically to keep this branch exercised.</para>
///
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
