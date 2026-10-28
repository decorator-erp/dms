using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using ApprovalState = Crm.Domain.ApprovalState;

namespace Crm.Application.Features.Briefs;

/// <summary>
/// A brief's rounds, read from its head snapshots (one per send). A round's stored outcome wins. A round
/// without one is either the current round, whose outcome is the brief's live state (pending, approved, or
/// changes requested; a Draft brief was edited after its decision, which tells nothing), or a round sent
/// before outcomes were recorded, which is <see cref="CrmBriefRoundOutcome.Unknown"/>. No backfill.
/// </summary>
internal static class BriefRounds
{
    public static async Task<Result<IReadOnlyList<CrmBriefRoundResponse>>> BuildAsync(
        CrmDbContext db, Guid briefId, bool includeVoided, CancellationToken ct)
    {
        ApprovalState? status = await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => b.Id == briefId)
            .Select(b => (ApprovalState?)b.Status)
            .FirstOrDefaultAsync(ct);
        if (status is null)
        {
            return Result.Failure<IReadOnlyList<CrmBriefRoundResponse>>(CrmBriefErrors.NotFound);
        }

        var rounds = await db.CrmBriefHeadSnapshots.AsNoTracking()
            .Where(s => s.BriefId == briefId)
            .OrderBy(s => s.Round)
            .Select(s => new { s.Round, s.CreatedAtUtc, s.Outcome, s.DecidedAtUtc })
            .ToListAsync(ct);
        int latest = rounds.Count == 0 ? 0 : rounds[^1].Round;

        return Result.Success<IReadOnlyList<CrmBriefRoundResponse>>([.. rounds
            .Select(r => new CrmBriefRoundResponse(
                r.Round, r.CreatedAtUtc, OutcomeOf(r.Outcome, r.Round == latest ? status.Value : null), r.DecidedAtUtc))
            // D-6: the portal also drops an older round whose outcome is Unknown (sent before outcomes were
            // recorded); round numbers can then have gaps there. The office sees every round.
            .Where(r => includeVoided || IsShownOnThePortal(r, latest))]);
    }

    private static bool IsShownOnThePortal(CrmBriefRoundResponse round, int latest) => round.Outcome switch
    {
        CrmBriefRoundOutcome.Voided => false,
        CrmBriefRoundOutcome.Unknown => round.Round == latest,
        _ => true,
    };

    private static CrmBriefRoundOutcome OutcomeOf(BriefRoundOutcome? stored, ApprovalState? currentRoundState) => stored switch
    {
        BriefRoundOutcome.Approved => CrmBriefRoundOutcome.Approved,
        BriefRoundOutcome.ChangesRequested => CrmBriefRoundOutcome.ChangesRequested,
        BriefRoundOutcome.Voided => CrmBriefRoundOutcome.Voided,
        _ => currentRoundState switch
        {
            ApprovalState.PendingApproval => CrmBriefRoundOutcome.Pending,
            ApprovalState.Approved => CrmBriefRoundOutcome.Approved,
            ApprovalState.Rejected => CrmBriefRoundOutcome.ChangesRequested,
            _ => CrmBriefRoundOutcome.Unknown,
        },
    };
}

internal sealed class GetCrmBriefRoundsQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmBriefRoundsQuery, IReadOnlyList<CrmBriefRoundResponse>>
{
    public Task<Result<IReadOnlyList<CrmBriefRoundResponse>>> HandleAsync(GetCrmBriefRoundsQuery query, CancellationToken ct = default) =>
        BriefRounds.BuildAsync(db, query.BriefId, includeVoided: true, ct);
}
