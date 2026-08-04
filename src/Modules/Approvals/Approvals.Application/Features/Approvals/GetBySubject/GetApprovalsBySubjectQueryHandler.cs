using Approvals.Contracts.Approvals;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Approvals.Application.Features.Approvals.GetBySubject;

internal sealed class GetApprovalsBySubjectQueryValidator : AbstractValidator<GetApprovalsBySubjectQuery>
{
    public GetApprovalsBySubjectQueryValidator() => RuleFor(x => x.SubjectId).NotEmpty();
}

/// <summary>The full trail for a subject (every round, decided or pending), newest round first — the
/// subject-agnostic replacement for the backend's own <c>GetApprovalsBySubjectQuery</c>.</summary>
internal sealed class GetApprovalsBySubjectQueryHandler(ApprovalsDbContext db)
    : IQueryHandler<GetApprovalsBySubjectQuery, IReadOnlyList<ApprovalTrailItemResponse>>
{
    public async Task<Result<IReadOnlyList<ApprovalTrailItemResponse>>> HandleAsync(
        GetApprovalsBySubjectQuery query, CancellationToken ct = default)
    {
        var subjectType = ApprovalEnumMapping.ToDomain(query.SubjectType);

        var approvals = await db.Approvals.AsNoTracking()
            .Where(a => a.SubjectType == subjectType && a.SubjectId == query.SubjectId)
            .OrderByDescending(a => a.Round)
            .ToListAsync(ct);

        List<ApprovalTrailItemResponse> items = [.. approvals.Select(a => new ApprovalTrailItemResponse(
            a.Id, ApprovalEnumMapping.ToContracts(a.SubjectType), a.SubjectId,
            ApprovalEnumMapping.ToContracts(a.Type), ApprovalEnumMapping.ToContracts(a.State),
            a.Decision.HasValue ? ApprovalEnumMapping.ToContracts(a.Decision.Value) : null,
            a.Comment, a.Round, a.DueAtUtc, a.DecidedAtUtc))];

        return Result.Success<IReadOnlyList<ApprovalTrailItemResponse>>(items);
    }
}
