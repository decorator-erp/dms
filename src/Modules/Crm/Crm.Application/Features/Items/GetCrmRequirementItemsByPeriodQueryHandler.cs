using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class GetCrmRequirementItemsByPeriodQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmRequirementItemsByPeriodQuery, IReadOnlyList<CrmRequirementItemResponse>>
{
    public async Task<Result<IReadOnlyList<CrmRequirementItemResponse>>> HandleAsync(
        GetCrmRequirementItemsByPeriodQuery query, CancellationToken ct = default)
    {
        List<CrmRequirementItemResponse> items = await db.CrmRequirementItems.AsNoTracking()
            .Where(i => i.PeriodId == query.PeriodId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new CrmRequirementItemResponse(
                i.Id, i.ProjectId, i.PeriodId, CrmEnumMapping.ToContracts(i.Category), i.SpaceTypeId, i.Room,
                i.Title, i.Description, CrmEnumMapping.ToContracts(i.Priority),
                i.SourceType != null ? CrmEnumMapping.ToContracts(i.SourceType.Value) : null, i.MeetingId,
                i.BriefId, i.NoteId, CrmEnumMapping.ToContracts(i.ApprovalStatus), i.SupersedesItemId,
                i.CreatedByEmployeeId, i.CreatedByContactId, i.ProjectTaskId))
            .ToListAsync(ct);

        return items;
    }
}
