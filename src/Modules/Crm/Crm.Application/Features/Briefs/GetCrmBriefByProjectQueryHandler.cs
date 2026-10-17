using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class GetCrmBriefByProjectQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmBriefByProjectQuery, CrmDesignBriefResponse>
{
    public async Task<Result<CrmDesignBriefResponse>> HandleAsync(GetCrmBriefByProjectQuery query, CancellationToken ct = default)
    {
        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.ProjectId == query.ProjectId, ct);
        if (brief is null)
        {
            return Result.Failure<CrmDesignBriefResponse>(CrmBriefErrors.NotFound);
        }

        var data = new Contracts.Briefs.CrmBriefHeadData(
            CrmEnumMapping.ToContracts(brief.UnitType), CrmEnumMapping.ToContracts(brief.UnitCondition),
            brief.Address, brief.FloorNo, brief.UnitsPerFloor, brief.AreaSqm,
            brief.BuildingKind is { } bk ? CrmEnumMapping.ToContracts(bk) : null, brief.IsLicensed,
            brief.Position is { } p ? CrmEnumMapping.ToContracts(p) : null,
            brief.ElevatorStatus is { } es ? CrmEnumMapping.ToContracts(es) : null,
            brief.ReceivesFrequentVisitors, brief.VisitorsNote, brief.UsedDesignerBefore,
            brief.DesignerExperience is { } de ? CrmEnumMapping.ToContracts(de) : null,
            brief.DesignerExperienceReason, CrmEnumMapping.ToContracts(brief.HiringReasons),
            brief.PreferredStyle, brief.PreferredColors,
            brief.FurnitureStatus is { } fs ? CrmEnumMapping.ToContracts(fs) : null, brief.ExpectedBudget,
            brief.TimelinePressure is { } tp ? CrmEnumMapping.ToContracts(tp) : null,
            brief.ArchitecturalRequests, brief.OtherNeeds, brief.IsRented,
            brief.FinishingPurpose is { } fp ? CrmEnumMapping.ToContracts(fp) : null);

        return new CrmDesignBriefResponse(
            brief.Id, brief.ProjectId, brief.ProjectStageId, brief.CreatedById, brief.LastMeetingId,
            CrmEnumMapping.ToContracts(brief.Status), data);
    }
}
