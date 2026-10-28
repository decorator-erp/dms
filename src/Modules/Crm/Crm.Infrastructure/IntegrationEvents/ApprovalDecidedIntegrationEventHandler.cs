using Approvals.Contracts;
using BuildingBlocks.Events;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Crm.Infrastructure.IntegrationEvents;

/// <summary>
/// CRM's own subscriber to <see cref="ApprovalDecidedIntegrationEvent"/>, filtered to
/// <see cref="ApprovalSubjectType.DesignBrief"/>/<see cref="ApprovalSubjectType.RequirementItem"/>/
/// <see cref="ApprovalSubjectType.BriefSpace"/> — CRM is the third subscriber to this event, after
/// Projects (<c>GateCondition</c>) and Documents (<c>ProjectDocument</c>), per Phase 3 lesson 2. The
/// filter is the very first thing this handler does, before any query, per
/// <c>docs/adding-a-module.md</c>'s subject-type-filtered subscription pattern.
///
/// <para><b>Replaces the backend's <c>ICrmApprovalCallbacks</c></b> — a DI-cycle workaround whose own
/// doc comment explains it exists only so the Approval domain service doesn't take a direct dependency
/// on CRM services (which themselves depend on the approval-request side, which would DI-cycle). The
/// outbox removes the need for that seam entirely: CRM subscribes to the SAME
/// <see cref="ApprovalDecidedIntegrationEvent"/> Projects and Documents already do, with no direct
/// reference to Approvals' internals in either direction.</para>
///
/// <para><see cref="ApprovalDecision.ChangesRequested"/> is mapped to each CRM entity's own
/// <c>MarkRejected</c> — CRM's ledger has no state distinct from Rejected that still permits
/// Update/Send again, and Rejected already IS exactly that "send it back for revision" state. This
/// diverges from the general in-repo convention (`ChangesRequested` → non-terminal PendingApproval,
/// per <c>Approvals.Contracts.ApprovalState</c>'s own remarks) because CRM's own state machine — ported
/// from the backend — has no fourth bucket to put it in; recorded here as a deliberate choice, not an
/// oversight.</para>
///
/// <para><b>Every decision is bound to its round.</b> The brief and the brief-space entry each record the
/// approval their current round was sent under (<c>PendingApprovalId</c>); the decision is applied only
/// when <see cref="ApprovalDecidedIntegrationEvent.ApprovalId"/> matches it. A decision for any other
/// approval — a superseded round, an orphan — is logged at Warning and dropped, never applied to
/// whatever the head holds now. The application itself lives in <see cref="CrmApprovalOutcomes"/>,
/// shared with the send handlers' recovery path.</para>
///
/// <para><b>Concurrency:</b> a <c>DbUpdateConcurrencyException</c> (the brief, entry and — A-9 — item carry an
/// <c>xmin</c> token) propagates out of this handler on purpose. <c>InboxDedupeDecorator</c> rolls the
/// whole delivery back, and <c>OutboxProcessor</c> records the failure and leaves the message
/// unprocessed, so the next poll re-runs it against the reloaded row, up to <c>Outbox:MaxAttempts</c>
/// (default 15) before the message is dead-lettered and logged.</para>
///
/// <para>Stages every write via the shared <see cref="CrmDbContext"/> and never calls its own
/// <c>SaveChangesAsync</c> to COMMIT — that stays <c>InboxDedupeDecorator</c>'s job. <b>E2-03
/// correction:</b> the brief-space and requirement-item paths DO each call <c>db.SaveChangesAsync</c>
/// once, mid-handler — needed so <c>PublishIfResolvedAsync</c>'s
/// <c>AsNoTracking</c> unresolved-count queries see the mutation just made on the SAME row they're about
/// to count, since a no-tracking query always issues fresh SQL rather than reading the change tracker.
/// Still entirely inside <c>InboxDedupeDecorator</c>'s one ambient transaction, so atomicity holds — the
/// same "multiple internal saves are fine, only the final commit matters" pattern that decorator's own
/// remarks already establish for <c>DomainEventDispatcher</c>'s drain loop.</para>
/// </summary>
internal sealed class ApprovalDecidedIntegrationEventHandler(
    CrmDbContext db,
    CrmApprovalOutcomes outcomes,
    ILogger<ApprovalDecidedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<ApprovalDecidedIntegrationEvent>
{
    public async Task HandleAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        switch (@event.SubjectType)
        {
            case ApprovalSubjectType.DesignBrief:
                await HandleDesignBriefAsync(@event, ct);
                return;
            case ApprovalSubjectType.BriefSpace:
                await HandleBriefSpaceAsync(@event, ct);
                return;
            case ApprovalSubjectType.RequirementItem:
                await HandleRequirementItemAsync(@event, ct);
                return;
            default:
                // Every other subject type (GateCondition, ProjectDocument, ...) belongs to a
                // different subscriber - a no-op here, not an error, per the shared-event convention.
                return;
        }
    }

    private async Task HandleDesignBriefAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        CrmDesignBrief? brief = await db.CrmDesignBriefs.FirstOrDefaultAsync(b => b.Id == @event.SubjectId, ct);
        if (brief is null)
        {
            return;
        }

        Result result = await outcomes.ApplyToBriefAsync(
            brief, @event.ApprovalId, @event.Decision, @event.OccurredAtUtc.UtcDateTime, ct);
        if (result.IsFailure)
        {
            LogIgnored(@event, brief.Status, brief.PendingApprovalId);
        }
    }

    private async Task HandleBriefSpaceAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        CrmBriefSpace? entry = await db.CrmBriefSpaces.FirstOrDefaultAsync(e => e.Id == @event.SubjectId, ct);
        if (entry is null)
        {
            return;
        }

        Result result = await outcomes.ApplyToBriefSpaceAsync(entry, @event.ApprovalId, @event.Decision, ct);
        if (result.Error == CrmBriefErrors.ConcurrencyConflict)
        {
            // A lost xmin race is not a stale decision: rethrow so the inbox rolls back and the next outbox
            // poll re-delivers against the reloaded row (the class remarks' retry path), never drop it.
            throw new DbUpdateConcurrencyException(
                $"BriefSpace {entry.Id} changed while applying approval {@event.ApprovalId}; retrying.");
        }

        if (result.IsFailure)
        {
            LogIgnored(@event, entry.ApprovalStatus, entry.PendingApprovalId);
        }
    }

    private async Task HandleRequirementItemAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        CrmRequirementItem? item = await db.CrmRequirementItems.FirstOrDefaultAsync(i => i.Id == @event.SubjectId, ct);
        if (item is null)
        {
            return;
        }

        // A-10 (B-M7): round-bound like a brief-space entry — a decision for any approval but the one the
        // item's current send is bound to is stale and ignored.
        Result result = await outcomes.ApplyToItemAsync(item, @event.ApprovalId, @event.Decision, ct);
        if (result.Error == CrmItemErrors.ConcurrentUpdate)
        {
            // Same retry path as the brief-space branch: never drop a decision on a lost xmin race.
            throw new DbUpdateConcurrencyException(
                $"RequirementItem {item.Id} changed while applying approval {@event.ApprovalId}; retrying.");
        }

        if (result.IsFailure)
        {
            LogIgnored(@event, item.ApprovalStatus, item.PendingApprovalId);
        }
    }

    private void LogIgnored(ApprovalDecidedIntegrationEvent @event, Domain.ApprovalState status, Guid? pendingApprovalId) =>
        logger.LogWarning(
            "Ignoring {Decision} from approval {ApprovalId} for {SubjectType} {SubjectId}: subject is {Status} "
            + "bound to approval {PendingApprovalId}",
            @event.Decision, @event.ApprovalId, @event.SubjectType, @event.SubjectId, status, pendingApprovalId);
}
