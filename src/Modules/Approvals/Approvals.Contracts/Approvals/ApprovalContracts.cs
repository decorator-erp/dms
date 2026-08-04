using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Approvals.Contracts.Approvals;

/// <summary>
/// Command/query wrapper types and DTOs for Approvals' own HTTP surface, living here rather than in
/// <c>Approvals.Application</c> for the same circular-reference reason established at Identity's G1-07
/// and followed by every module since: <c>Approvals.Infrastructure</c>'s endpoints need to name these
/// exact types to resolve <c>ICommandHandler&lt;TCommand,TResponse&gt;</c>/
/// <c>IQueryHandler&lt;TQuery,TResponse&gt;</c> from DI, and <c>Approvals.Application</c> already has a
/// real <c>ProjectReference</c> to <c>Approvals.Infrastructure</c> — the reverse reference would be
/// circular.
/// </summary>
public sealed record ApprovalTrailItemResponse(
    Guid Id, ApprovalSubjectType SubjectType, Guid SubjectId, ApprovalType Type, ApprovalState State,
    ApprovalDecision? Decision, string? Comment, int Round, DateTimeOffset? DueAtUtc,
    DateTimeOffset? DecidedAtUtc);

public sealed record PendingApprovalResponse(
    Guid Id, ApprovalSubjectType SubjectType, Guid SubjectId, ApprovalType Type, int Round,
    DateTimeOffset? DueAtUtc);

/// <summary>
/// <c>Comment</c> is required by the handler when <c>Decision</c> is <see cref="ApprovalDecision.Rejected"/>
/// or <see cref="ApprovalDecision.ChangesRequested"/> (<c>ApprovalErrors.CommentRequired</c>). The
/// deciding user's id is never a field here — the handler reads it from <c>ICurrentUser</c>, the same
/// fix Identity's G1-06 applied to its own override commands, so it can never go stale mid-request or be
/// spoofed from the request body.
/// </summary>
public sealed record DecideApprovalCommand(Guid ApprovalId, ApprovalDecision Decision, string? Comment)
    : ICommand;

public sealed record GetMyPendingApprovalsQuery(int PageNumber, int PageSize)
    : IQuery<PagedResult<PendingApprovalResponse>>;

public sealed record GetApprovalsBySubjectQuery(ApprovalSubjectType SubjectType, Guid SubjectId)
    : IQuery<IReadOnlyList<ApprovalTrailItemResponse>>;
