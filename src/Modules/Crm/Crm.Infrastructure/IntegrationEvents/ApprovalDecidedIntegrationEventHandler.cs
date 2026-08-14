using Approvals.Contracts;
using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

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
/// <para>Stages every write via the shared <see cref="CrmDbContext"/>, never calls its own
/// <c>SaveChangesAsync</c>, per <c>InboxDedupeDecorator</c>'s contract.</para>
/// </summary>
internal sealed class ApprovalDecidedIntegrationEventHandler(CrmDbContext db, ICrmGateReader gateReader)
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
        var brief = await db.CrmDesignBriefs.FirstOrDefaultAsync(b => b.Id == @event.SubjectId, ct);
        if (brief is null) return;

        if (@event.Decision == ApprovalDecision.Approved)
        {
            var result = brief.MarkApproved();
            if (result.IsFailure) return;

            if (brief.ProjectStageId is { } stageId)
            {
                db.Outbox().Enqueue(new CrmBriefApprovedIntegrationEvent(
                    Guid.NewGuid(), brief.Id, stageId, DateTimeOffset.UtcNow));
            }
        }
        else
        {
            brief.MarkRejected();
        }
    }

    private async Task HandleBriefSpaceAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        var entry = await db.CrmBriefSpaces.FirstOrDefaultAsync(e => e.Id == @event.SubjectId, ct);
        if (entry is null) return;

        if (@event.Decision == ApprovalDecision.Approved)
            entry.MarkApproved();
        else
            entry.MarkRejected();

        var brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == entry.BriefId, ct);
        await PublishIfResolvedAsync(brief?.ProjectStageId, entry.ProjectTaskId, ct);
    }

    private async Task HandleRequirementItemAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        var item = await db.CrmRequirementItems.FirstOrDefaultAsync(i => i.Id == @event.SubjectId, ct);
        if (item is null) return;

        if (@event.Decision == ApprovalDecision.Approved)
            item.MarkApproved();
        else
            item.MarkRejected();

        var period = await db.CrmRequirementPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.PeriodId, ct);
        await PublishIfResolvedAsync(period?.ProjectStageId, item.ProjectTaskId, ct);
    }

    /// <summary>Checks both scopes the decided row could belong to and publishes
    /// <see cref="CrmItemsResolvedIntegrationEvent"/> once, per scope, only when its unresolved count
    /// has reached zero — a stage-owned gate and a task-owned gate for the SAME entry are both real
    /// possibilities (a brief-space entry always has a stage via its brief; it may ALSO carry its own
    /// <c>ProjectTaskId</c>), so this checks both rather than assuming exactly one applies.</summary>
    private async Task PublishIfResolvedAsync(Guid? projectStageId, Guid? projectTaskId, CancellationToken ct)
    {
        if (projectStageId is { } stageId && await gateReader.CountUnresolvedForStageAsync(stageId, ct) == 0)
        {
            db.Outbox().Enqueue(new CrmItemsResolvedIntegrationEvent(
                Guid.NewGuid(), stageId, null, DateTimeOffset.UtcNow));
        }

        if (projectTaskId is { } taskId && await gateReader.CountUnresolvedForTaskAsync(taskId, ct) == 0)
        {
            db.Outbox().Enqueue(new CrmItemsResolvedIntegrationEvent(
                Guid.NewGuid(), null, taskId, DateTimeOffset.UtcNow));
        }
    }
}
