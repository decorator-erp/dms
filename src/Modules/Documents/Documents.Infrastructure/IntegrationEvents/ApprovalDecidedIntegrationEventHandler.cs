using Approvals.Contracts;
using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using Documents.Contracts;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Infrastructure.IntegrationEvents;

/// <summary>
/// Makes <c>ApprovalSubjectType.ProjectDocument</c> real on the consuming side (G7-07) — lives in
/// Infrastructure, not Application, same "Infrastructure owns handlers that call another module's
/// contract" reasoning <c>Projects.Infrastructure.ApprovalDecidedIntegrationEventHandler</c>'s own
/// remarks document. Filtered to <see cref="ApprovalSubjectType.ProjectDocument"/> — every other
/// subject type (<see cref="ApprovalSubjectType.GateCondition"/> and friends) belongs to Projects and is
/// left unhandled here, marked processed via the zero-*matching*-handler path (R2), the exact mirror of
/// how Projects' own handler ignores <c>ProjectDocument</c>.
///
/// <see cref="ApprovalDecision.Approved"/> locks the decided document (<c>SubjectId</c> — the specific
/// version that was under review) and enqueues <see cref="DocumentApprovedIntegrationEvent"/> keyed by
/// its lineage root, for <c>Projects.Infrastructure</c> to satisfy matching <c>DocumentApproved</c>
/// gates. <see cref="ApprovalDecision.Rejected"/>/<see cref="ApprovalDecision.ChangesRequested"/> do
/// nothing — no lock, no event, matching <c>GateCondition</c>'s own precedent that
/// <c>ChangesRequested</c> makes no forward call at all (a rework loop, not a rejection).
///
/// Stages the write only — per <c>InboxDedupeDecorator</c>'s contract, never calls its own
/// <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class ApprovalDecidedIntegrationEventHandler(DocumentsDbContext db)
    : IIntegrationEventHandler<ApprovalDecidedIntegrationEvent>
{
    public async Task HandleAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        if (@event.SubjectType != ApprovalSubjectType.ProjectDocument)
        {
            return;
        }

        if (@event.Decision != ApprovalDecision.Approved)
        {
            return;
        }

        ProjectDocument? document = await db.ProjectDocuments.FirstOrDefaultAsync(d => d.Id == @event.SubjectId, ct);
        if (document is null)
        {
            return;
        }

        Result lockResult = document.Lock();
        if (lockResult.IsFailure)
        {
            return; // already locked - nothing further to do, not an error worth surfacing here.
        }

        db.Outbox().Enqueue(new DocumentApprovedIntegrationEvent(
            Guid.NewGuid(), document.RootDocumentId, document.ProjectId, DateTimeOffset.UtcNow));
    }
}
