using BuildingBlocks.Messaging;
using Crm.Contracts.DesignProfile;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.DesignProfile;

/// <summary>Office-facing read gated by <c>crm.design_profile.read</c>. Deliberately excludes
/// <c>ExpectedBudget</c> — see the response type's own remarks.</summary>
internal sealed class GetClientDesignProfileQueryHandler(CrmDbContext db)
    : IQueryHandler<GetClientDesignProfileQuery, ClientDesignProfileResponse>
{
    public async Task<Result<ClientDesignProfileResponse>> HandleAsync(
        GetClientDesignProfileQuery query, CancellationToken ct = default)
    {
        var brief = await db.CrmDesignBriefs.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ProjectId == query.ProjectId, ct);
        if (brief is null)
            return Result.Failure<ClientDesignProfileResponse>(CrmBriefErrors.NotFound);

        return new ClientDesignProfileResponse(
            brief.ProjectId, brief.PreferredStyle, brief.PreferredColors,
            brief.FurnitureStatus is { } fs ? CrmEnumMapping.ToContracts(fs) : null,
            CrmEnumMapping.ToContracts(brief.HiringReasons), brief.ArchitecturalRequests, brief.OtherNeeds);
    }
}
