using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Contracts.Surveys;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Surveys;

/// <summary>
/// Pulled forward from S1-07 deliberately, and it is the only instance-layer handler in S1-06.
///
/// <para><b>Why:</b> <c>SiteScopeRegistration.DecorateIfRegistered</c> skips any <c>ISiteScoped</c> type
/// with no handler registered — tolerance that is correct, and that also means registering the decorator
/// with zero scoped handlers decorates <b>nothing</b>, succeeds silently, and looks identical to a
/// working installation. That is precisely how G5-07 shipped <c>ProjectScopeRegistration</c> scanning
/// the wrong assembly: it found zero types, wired nothing, and every project-scoped command ran with no
/// access control at all. One real scoped consumer is the minimum needed to prove the decorator is
/// applied rather than merely registered — see <c>SiteScopeDecoratorTests</c>.</para>
/// </summary>
internal sealed class GetSiteByIdQueryHandler(SitesDbContext db)
    : IQueryHandler<GetSiteByIdQuery, SiteDetailResponse>
{
    public async Task<Result<SiteDetailResponse>> HandleAsync(
        GetSiteByIdQuery query, CancellationToken ct = default)
    {
        var site = await db.Sites.AsNoTracking()
            .Where(s => s.Id == query.SiteId)
            .Select(s => new
            {
                s.Id,
                s.ProjectId,
                s.Code,
                s.Status,
                s.Address,
                s.UnitType,
                s.UnitCondition,
                s.AreaSqm,
                s.FloorNo,
                s.UnitsPerFloor,
                s.BuildingKind,
                s.Position,
                s.ElevatorStatus,
                s.IsLicensed,
                s.IsRented,
                s.UtilityMetersNote,
                s.SurveyedOn,
                s.Notes,
                s.CreatedById,
                s.SubmittedAtUtc,
                s.DecidedAtUtc,
                s.DecidedById,
                s.DecisionNote,
                s.IsActive,
                s.CreatedAtUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        // A second query rather than a correlated subquery per module: the record counts are a GROUP BY
        // over this site's own rows, joined to the module for its code.
        List<SiteModuleRecordCountResponse> recordCounts = await (
            from record in db.SiteRecords.AsNoTracking()
            join module in db.SiteModules.AsNoTracking() on record.ModuleId equals module.Id
            where record.SiteId == query.SiteId
            group module by new { module.Id, module.Code, module.SortOrder } into grouped
            orderby grouped.Key.SortOrder, grouped.Key.Code
            select new SiteModuleRecordCountResponse(grouped.Key.Id, grouped.Key.Code, grouped.Count()))
            .ToListAsync(ct);

        return new SiteDetailResponse(
            site.Id, site.ProjectId, site.Code, SiteEnumMapping.ToContracts(site.Status),
            site.Address, SiteEnumMapping.ToContracts(site.UnitType),
            SiteEnumMapping.ToContracts(site.UnitCondition), site.AreaSqm,
            site.FloorNo, site.UnitsPerFloor, SiteEnumMapping.ToContracts(site.BuildingKind),
            SiteEnumMapping.ToContracts(site.Position), SiteEnumMapping.ToContracts(site.ElevatorStatus),
            site.IsLicensed, site.IsRented, site.UtilityMetersNote, site.SurveyedOn, site.Notes,
            site.CreatedById, site.SubmittedAtUtc, site.DecidedAtUtc, site.DecidedById,
            site.DecisionNote, site.IsActive, site.CreatedAtUtc, recordCounts);
    }
}
