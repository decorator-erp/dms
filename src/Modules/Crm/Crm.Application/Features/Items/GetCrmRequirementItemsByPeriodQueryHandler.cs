using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Items;

/// <summary>A-13 (B-L4): same paging rules as the lead list.</summary>
internal sealed class GetCrmRequirementItemsByPeriodQueryValidator : AbstractValidator<GetCrmRequirementItemsByPeriodQuery>
{
    public GetCrmRequirementItemsByPeriodQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class GetCrmRequirementItemsByPeriodQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmRequirementItemsByPeriodQuery, PagedResult<CrmRequirementItemResponse>>
{
    public async Task<Result<PagedResult<CrmRequirementItemResponse>>> HandleAsync(
        GetCrmRequirementItemsByPeriodQuery query, CancellationToken ct = default)
    {
        IQueryable<CrmRequirementItem> matching = db.CrmRequirementItems.AsNoTracking().Where(i => i.PeriodId == query.PeriodId);
        int total = await matching.CountAsync(ct);
        List<CrmRequirementItemResponse> items = await matching
            .OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(i => new CrmRequirementItemResponse(
                i.Id, i.ProjectId, i.PeriodId, CrmEnumMapping.ToContracts(i.Category), i.SpaceTypeId, i.Room,
                i.Title, i.Description, CrmEnumMapping.ToContracts(i.Priority),
                i.SourceType != null ? CrmEnumMapping.ToContracts(i.SourceType.Value) : null, i.MeetingId,
                i.BriefId, i.NoteId, CrmEnumMapping.ToContracts(i.ApprovalStatus), i.SupersedesItemId,
                i.CreatedByEmployeeId, i.CreatedByContactId, i.ProjectTaskId))
            .ToListAsync(ct);

        return new PagedResult<CrmRequirementItemResponse>(items, query.PageNumber, query.PageSize, total);
    }
}
