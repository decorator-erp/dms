using Approvals.Contracts;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Infrastructure.Services;

/// <summary>
/// Applies one approval decision to a brief head or a brief-space entry, and stages what follows from it
/// (the stage-gate events). Shared by the two places a decision reaches CRM: the normal path,
/// <c>ApprovalDecidedIntegrationEventHandler</c>, and the send handlers' recovery path, where
/// <c>IApprovalRequester.RequestAsync</c> returns an approval that was decided while CRM's own row never
/// committed — the decided event found nothing bound to it and was dropped, so the resend applies it.
///
/// <para><see cref="ApprovalDecision.ChangesRequested"/> maps to <c>MarkRejected</c>; see
/// <c>ApprovalDecidedIntegrationEventHandler</c>'s remarks for why CRM has no fourth bucket.</para>
/// </summary>
internal sealed class CrmApprovalOutcomes(CrmDbContext db, ICrmGateReader gateReader)
{
    /// <summary>Fails (and changes nothing) unless <paramref name="approvalId"/> is the approval the
    /// brief's current round was sent under. On success, also records the outcome on that round's snapshot
    /// (GAP-2): snapshots are created only on send, so the round being decided is the brief's highest one.
    /// <paramref name="decidedAtUtc"/> is the decision's time where the caller knows it.</summary>
    public async Task<Result> ApplyToBriefAsync(
        CrmDesignBrief brief, Guid approvalId, ApprovalDecision decision, DateTime decidedAtUtc, CancellationToken ct)
    {
        bool approved = decision == ApprovalDecision.Approved;
        Result result = approved ? brief.MarkApproved(approvalId) : brief.MarkRejected(approvalId);
        if (result.IsFailure)
        {
            return result;
        }

        CrmBriefHeadSnapshot? round = await db.CrmBriefHeadSnapshots
            .Where(s => s.BriefId == brief.Id)
            .OrderByDescending(s => s.Round)
            .FirstOrDefaultAsync(ct);
        round?.RecordOutcome(approved ? BriefRoundOutcome.Approved : BriefRoundOutcome.ChangesRequested, decidedAtUtc);

        if (approved)
        {
            db.Outbox().Enqueue(new CrmBriefApprovedIntegrationEvent(
                Guid.NewGuid(), brief.Id, brief.ProjectStageId, DateTimeOffset.UtcNow));
        }

        return Result.Success();
    }

    /// <summary>Same binding rule as <see cref="ApplyToBriefAsync"/>. On success, flushes (a lost
    /// <c>xmin</c> race is <see cref="CrmBriefErrors.ConcurrencyConflict"/>) and publishes
    /// <see cref="CrmItemsResolvedIntegrationEvent"/> for any scope the entry just resolved.</summary>
    public async Task<Result> ApplyToBriefSpaceAsync(
        CrmBriefSpace entry, Guid approvalId, ApprovalDecision decision, CancellationToken ct)
    {
        Result result = decision == ApprovalDecision.Approved
            ? entry.MarkApproved(approvalId)
            : entry.MarkRejected(approvalId);
        if (result.IsFailure)
        {
            return result;
        }

        // Flush now: PublishIfResolvedAsync's unresolved counts are AsNoTracking queries, which hit the
        // database rather than the change tracker, so without this they still see THIS row's
        // pre-decision status and can never reach zero on the decision that resolves the last one. Still
        // inside the caller's one ambient transaction (InboxDedupeDecorator's, or the command pipeline's).
        // Through BriefWrites, so an xmin race on the send path is a 409, not a 500; the decided handler
        // turns that 409 back into a retry (see ApprovalDecidedIntegrationEventHandler).
        Result saved = await BriefWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return saved;
        }

        Guid? stageId = await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => b.Id == entry.BriefId)
            .Select(b => (Guid?)b.ProjectStageId)
            .FirstOrDefaultAsync(ct);
        await PublishIfResolvedAsync(stageId, entry.ProjectTaskId, ct);

        return Result.Success();
    }

    /// <summary>Checks both scopes the decided row could belong to and publishes
    /// <see cref="CrmItemsResolvedIntegrationEvent"/> once, per scope, only when its unresolved count
    /// has reached zero — a stage-owned gate and a task-owned gate for the SAME entry are both real
    /// possibilities (a brief-space entry always has a stage via its brief; it may ALSO carry its own
    /// <c>ProjectTaskId</c>), so this checks both rather than assuming exactly one applies.</summary>
    public async Task PublishIfResolvedAsync(Guid? projectStageId, Guid? projectTaskId, CancellationToken ct)
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
