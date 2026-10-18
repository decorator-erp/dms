using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

/// <summary>Obsolete route, kept for the frontend: a project has one brief per stage, so this returns
/// the most recently created one — deterministic, unlike the unordered <c>FirstOrDefault</c> it
/// replaces. Prefer <see cref="GetCrmBriefByStageQueryHandler"/>.</summary>
internal sealed class GetCrmBriefByProjectQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmBriefByProjectQuery, CrmDesignBriefResponse>
{
    public async Task<Result<CrmDesignBriefResponse>> HandleAsync(GetCrmBriefByProjectQuery query, CancellationToken ct = default)
    {
        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => b.ProjectId == query.ProjectId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (brief is null)
        {
            return Result.Failure<CrmDesignBriefResponse>(CrmBriefErrors.NotFound);
        }

        return BriefMapping.ToResponse(brief);
    }
}

internal sealed class GetCrmBriefByStageQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmBriefByStageQuery, CrmDesignBriefResponse>
{
    public async Task<Result<CrmDesignBriefResponse>> HandleAsync(GetCrmBriefByStageQuery query, CancellationToken ct = default)
    {
        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ProjectStageId == query.StageId, ct);
        return brief is null
            ? Result.Failure<CrmDesignBriefResponse>(CrmBriefErrors.NotFound)
            : BriefMapping.ToResponse(brief);
    }
}
