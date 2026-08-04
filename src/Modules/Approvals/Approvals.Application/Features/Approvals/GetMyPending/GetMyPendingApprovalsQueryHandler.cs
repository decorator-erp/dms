using Approvals.Contracts.Approvals;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Domain = Approvals.Domain;

namespace Approvals.Application.Features.Approvals.GetMyPending;

internal sealed class GetMyPendingApprovalsQueryValidator : AbstractValidator<GetMyPendingApprovalsQuery>
{
    public GetMyPendingApprovalsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>
/// The canonical "my inbox" query — approvals where the current user is one of the eligible
/// <see cref="Domain.Approval.Approvers"/> and no decision has been made yet. Matches the backend's
/// <c>GetMyPendingApprovalsQuery</c> (queries <c>Approvals</c> directly, not a gate-driven projection) —
/// the backend's two other, gate-driven inbox queries (<c>GetClientApprovalRequestsQuery</c>,
/// <c>GetManagerApprovalRequestsQuery</c>) are Projects/CRM-shaped and belong to those modules, not
/// ported here.
/// </summary>
internal sealed class GetMyPendingApprovalsQueryHandler(ApprovalsDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyPendingApprovalsQuery, PagedResult<PendingApprovalResponse>>
{
    public async Task<Result<PagedResult<PendingApprovalResponse>>> HandleAsync(
        GetMyPendingApprovalsQuery query, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
            return Domain.ApprovalErrors.Unauthenticated;

        var userId = currentUser.UserId;
        var baseQuery = db.Approvals.AsNoTracking()
            .Where(a => a.Decision == null && a.Approvers.Any(ap => ap.ApproverUserId == userId));

        var totalCount = await baseQuery.CountAsync(ct);

        var approvals = await baseQuery
            .OrderBy(a => a.DueAtUtc ?? DateTimeOffset.MaxValue)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = approvals.Select(a => new PendingApprovalResponse(
            a.Id, ApprovalEnumMapping.ToContracts(a.SubjectType), a.SubjectId,
            ApprovalEnumMapping.ToContracts(a.Type), a.Round, a.DueAtUtc)).ToList();

        return new PagedResult<PendingApprovalResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
