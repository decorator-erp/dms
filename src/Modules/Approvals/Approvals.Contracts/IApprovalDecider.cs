using SharedKernel;

namespace Approvals.Contracts;

/// <summary>
/// B1 (Option A): the client's own decision on an approval whose approver set names them as a
/// <see cref="ApproverKind.Client"/>. <b>Portal only</b> — this must be called only by CRM's portal decide
/// command (<c>DecideMyPortalApprovalCommandHandler</c>) after it has proven the caller owns the approval's
/// subject. Nothing here re-checks that ownership: Approvals knows no client or project. An architecture
/// test pins the one allowed caller.
///
/// <para>Same synchronous cross-module write shape as <see cref="IApprovalRequester.RequestAsync"/>: the
/// decision and its <see cref="ApprovalDecidedIntegrationEvent"/> commit together in Approvals' own
/// transaction, and the subject's module reacts through the outbox exactly as it does to an office
/// decision.</para>
/// </summary>
public interface IApprovalDecider
{
    /// <summary>
    /// Decides <paramref name="approvalId"/> as the client <paramref name="appUserId"/>. Errors:
    /// <c>approvals.NotFound</c> (404) when the approval does not exist <b>or</b> does not name the caller
    /// as a client approver (never 403 — the caller learns nothing about approvals that aren't theirs);
    /// <c>approvals.CommentRequired</c> (400) for <see cref="ApprovalDecision.Rejected"/>/
    /// <see cref="ApprovalDecision.ChangesRequested"/> without a comment; <c>approvals.CommentTooLong</c>
    /// (400) past 2000 characters; <c>approvals.AlreadyDecided</c> (409) when it was decided first — the
    /// row is locked, so of two parallel decisions exactly one wins.
    /// </summary>
    Task<Result> DecideAsClientAsync(
        Guid approvalId, string appUserId, ApprovalDecision decision, string? comment, CancellationToken ct);
}
