using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

/// <summary>The FR-029 per-round head diff, for either audience: round N's snapshot against round
/// N-1's (none in round 1). Shared by the office query and the portal one, which differ only in who may
/// ask and what <see cref="BriefDiffAudience"/> they get.</summary>
internal static class BriefRoundDiffs
{
    public static async Task<Result<BriefHeadDiffResponse>> BuildAsync(
        CrmDbContext db, Guid briefId, int round, BriefDiffAudience audience, CancellationToken ct)
    {
        List<CrmBriefHeadSnapshot> snapshots = await db.CrmBriefHeadSnapshots.AsNoTracking()
            .Where(s => s.BriefId == briefId && (s.Round == round || s.Round == round - 1))
            .ToListAsync(ct);

        CrmBriefHeadSnapshot? current = snapshots.FirstOrDefault(s => s.Round == round);
        if (current is null)
        {
            return Result.Failure<BriefHeadDiffResponse>(CrmBriefErrors.RoundNotFound);
        }

        CrmBriefHeadSnapshot? previous = snapshots.FirstOrDefault(s => s.Round == round - 1);
        return new BriefHeadDiffResponse(
            briefId, round, previous?.Round,
            [.. current.DiffFrom(previous, audience).Select(c => new BriefFieldChangeResponse(c.Field, c.PreviousValue, c.CurrentValue))]);
    }
}

/// <summary>Office audience — project scope is <c>CrmProjectScopeDecorator</c>'s.</summary>
internal sealed class GetCrmBriefRoundDiffQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmBriefRoundDiffQuery, BriefHeadDiffResponse>
{
    public Task<Result<BriefHeadDiffResponse>> HandleAsync(GetCrmBriefRoundDiffQuery query, CancellationToken ct = default) =>
        BriefRoundDiffs.BuildAsync(db, query.BriefId, query.Round, BriefDiffAudience.Office, ct);
}
