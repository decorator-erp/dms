using Approvals.Contracts;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Domain = Approvals.Domain;

namespace Approvals.Infrastructure.Services;

/// <summary>
/// B1.1: the implementation of <see cref="IApprovalDecider"/>, beside <see cref="ApprovalRequester"/> for
/// the same reason — a synchronous cross-module write exposed through the owning module's Contracts.
///
/// <para><b>Serialization.</b> Through <see cref="ApprovalLocks.DecideLockedAsync"/>, the same row lock the
/// office <c>DecideApprovalCommandHandler</c> takes, so a portal decision and an office override serialize
/// against each other: one wins, the other is <c>AlreadyDecided</c> (409).</para>
///
/// <para><b>Who may decide.</b> Only an approver entry of kind <see cref="ApproverKind.Client"/> naming the
/// caller. An unknown approval and one the caller is not a client approver on are the same 404, so the
/// portal learns nothing about approvals that aren't theirs. Never an override.</para>
///
/// <para>The decision and its <see cref="ApprovalDecidedIntegrationEvent"/> are saved together, exactly as
/// the office <c>DecideApprovalCommandHandler</c> does.</para>
/// </summary>
internal sealed class ApprovalDecider(ApprovalsDbContext db) : IApprovalDecider
{
    private const int MaxCommentLength = 2000;

    public Task<Result> DecideAsClientAsync(
        Guid approvalId, string appUserId, ApprovalDecision decision, string? comment, CancellationToken ct) =>
        ApprovalLocks.DecideLockedAsync(db, approvalId, () => DecideAsync(approvalId, appUserId, decision, comment, ct), ct);

    private async Task<Result> DecideAsync(
        Guid approvalId, string appUserId, ApprovalDecision decision, string? comment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appUserId))
        {
            return Domain.ApprovalErrors.NotFound;
        }

        // The lock is already held (ApprovalLocks): this load reads the row as the previous holder committed it.
        Domain.Approval? approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);
        if (approval is null
            || !approval.Approvers.Any(a => a.Kind == Domain.ApproverKind.Client && a.ApproverUserId == appUserId))
        {
            return Domain.ApprovalErrors.NotFound;
        }

        if (comment is { Length: > MaxCommentLength })
        {
            return Domain.ApprovalErrors.CommentTooLong;
        }

        Result decided = approval.Decide(appUserId, ApprovalRequester.ToDomain(decision), comment, isOverride: false);
        if (decided.IsFailure)
        {
            return decided;
        }

        db.Outbox().Enqueue(new ApprovalDecidedIntegrationEvent(
            Guid.NewGuid(), approval.Id, ApprovalRequester.ToContracts(approval.SubjectType), approval.SubjectId,
            decision, appUserId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
