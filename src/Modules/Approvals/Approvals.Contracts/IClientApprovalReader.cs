using BuildingBlocks.Pagination;

namespace Approvals.Contracts;

/// <summary>B1.3: one approval whose approver set names a client login. <see cref="DecidedByUserId"/> lets the
/// caller tell the client's own decision (and comment) from an office override.</summary>
public sealed record ClientApprovalRow(
    Guid Id, ApprovalSubjectType SubjectType, Guid SubjectId, DateTime RequestedAtUtc, DateTimeOffset? DueAtUtc,
    ApprovalDecision? Decision, DateTimeOffset? DecidedAtUtc, string? DecidedByUserId, string? Comment);

/// <summary>
/// B1.3 (C5): the client's approvals inbox, paged on the Approvals side so the page is cut in SQL. A many-read
/// by construction (the "one returns many" rule is satisfied by the page itself).
/// </summary>
public interface IClientApprovalReader
{
    /// <summary>Approvals with a <see cref="ApproverKind.Client"/> approver entry for
    /// <paramref name="appUserId"/>, limited to <paramref name="subjectTypes"/>. <paramref name="decided"/>
    /// false: undecided, newest request first; true: decided (any decision), newest decision first.
    /// <paramref name="pageNumber"/> from 1; the caller bounds <paramref name="pageSize"/>.</summary>
    Task<PagedResult<ClientApprovalRow>> GetForClientApproverAsync(
        string appUserId, IReadOnlyCollection<ApprovalSubjectType> subjectTypes, bool decided,
        int pageNumber, int pageSize, CancellationToken ct);
}
