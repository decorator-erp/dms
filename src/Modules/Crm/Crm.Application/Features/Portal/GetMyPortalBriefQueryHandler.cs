using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

/// <summary>
/// G9-14: the Portal slice — the only consumer of <see cref="ICrmAccessGuard"/>'s client-actor branch
/// this phase gives a real caller and a real test, per the plan's decision 3. <see cref="GetMyPortalBriefQuery"/>
/// is <c>ICrmScoped</c>, so <c>CrmScopeDecorator</c> already rejected a caller with no CRM identity at
/// all before this handler runs; this handler re-resolves the actor anyway (a small, deliberate
/// redundancy) because it needs the resolved <c>ClientId</c> itself, which the decorator does not pass
/// through — the decorator only gates, it never hands its result to the inner handler.
///
/// <para><c>GetMyPortalMeetingsQuery</c> was the other Portal operation left unimplemented at G9-14,
/// for the reason this comment used to record: <c>IProjectDirectory</c> had no client-scoped project
/// lookup, and Meetings had no Application layer at all (a real gap, recorded in G9-14's own
/// PROGRESS.md entry and closed at M1-01/M1-02). Both blockers are gone — see
/// <c>GetMyPortalMeetingsQueryHandler</c>.
///
/// <c>DecideMyPortalApprovalCommand</c> remains deferred: it would route through
/// <c>Approvals.Contracts</c>, whose <c>approval_approvers</c> ledger is employee-scoped in this port —
/// giving a client actor a decision path there is an Approvals-owned design question, not an
/// endpoint-wiring one. See M1-02's Scope-boundaries row.</para>
/// </summary>
internal sealed class GetMyPortalBriefQueryHandler(CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory)
    : IQueryHandler<GetMyPortalBriefQuery, PortalBriefViewResponse>
{
    public async Task<Result<PortalBriefViewResponse>> HandleAsync(GetMyPortalBriefQuery query, CancellationToken ct = default)
    {
        Result<Guid> clientId = await PortalClients.ResolveClientIdAsync(guard, query.AppUserId, ct);
        if (clientId.IsFailure)
        {
            return Result.Failure<PortalBriefViewResponse>(clientId.Error);
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(query.ProjectId, ct);
        if (project is null || project.ClientId != clientId.Value)
        {
            return Result.Failure<PortalBriefViewResponse>(CrmPortalErrors.NotFound);
        }

        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => b.ProjectId == query.ProjectId && (query.StageId == null || b.ProjectStageId == query.StageId))
            .OrderByDescending(b => b.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (brief is null)
        {
            return Result.Failure<PortalBriefViewResponse>(CrmPortalErrors.NotFound);
        }

        CrmBriefHeadSnapshot? latest = await db.CrmBriefHeadSnapshots.AsNoTracking()
            .Where(s => s.BriefId == brief.Id)
            .OrderByDescending(s => s.Round)
            .FirstOrDefaultAsync(ct);

        List<CrmBriefSpace> entries = await db.CrmBriefSpaces.AsNoTracking()
            .Where(e => e.BriefId == brief.Id && e.ApprovalStatus != ApprovalState.Draft)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);

        return new PortalBriefViewResponse(
            latest is null ? null : ToHeadBlock(brief, latest),
            BriefMapping.ToSpaceResponses(entries));
    }

    private static BriefHeadBlock ToHeadBlock(CrmDesignBrief brief, CrmBriefHeadSnapshot s) => new(
        brief.Id, brief.ProjectId, brief.ProjectStageId, CrmEnumMapping.ToContracts(brief.Status), s.Round,
        CrmEnumMapping.ToContracts(s.UnitType), CrmEnumMapping.ToContracts(s.UnitCondition), s.Address, s.FloorNo,
        s.UnitsPerFloor, s.AreaSqm,
        s.BuildingKind is { } bk ? CrmEnumMapping.ToContracts(bk) : null, s.IsLicensed,
        s.Position is { } p ? CrmEnumMapping.ToContracts(p) : null,
        s.ElevatorStatus is { } es ? CrmEnumMapping.ToContracts(es) : null,
        s.ReceivesFrequentVisitors, s.VisitorsNote, s.UsedDesignerBefore,
        s.DesignerExperience is { } de ? CrmEnumMapping.ToContracts(de) : null,
        s.DesignerExperienceReason, CrmEnumMapping.ToContracts(s.HiringReasons), s.PreferredStyle, s.PreferredColors,
        s.FurnitureStatus is { } fs ? CrmEnumMapping.ToContracts(fs) : null,
        s.TimelinePressure is { } tp ? CrmEnumMapping.ToContracts(tp) : null,
        s.ArchitecturalRequests, s.OtherNeeds,
        s.Sector is { } sector ? CrmEnumMapping.ToContracts(sector) : null, s.IsRented,
        s.FinishingPurpose is { } fp ? CrmEnumMapping.ToContracts(fp) : null);
}

/// <summary>The client audience's round diff: only for a brief on one of the caller's own projects
/// (else 404, never 403 — same reasoning as <see cref="GetMyPortalBriefQueryHandler"/>).</summary>
internal sealed class GetMyPortalBriefRoundDiffQueryHandler(CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory)
    : IQueryHandler<GetMyPortalBriefRoundDiffQuery, BriefHeadDiffResponse>
{
    public async Task<Result<BriefHeadDiffResponse>> HandleAsync(GetMyPortalBriefRoundDiffQuery query, CancellationToken ct = default)
    {
        Result<Guid> clientId = await PortalClients.ResolveClientIdAsync(guard, query.AppUserId, ct);
        if (clientId.IsFailure)
        {
            return Result.Failure<BriefHeadDiffResponse>(clientId.Error);
        }

        Guid? projectId = await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => b.Id == query.BriefId)
            .Select(b => (Guid?)b.ProjectId)
            .FirstOrDefaultAsync(ct);
        ProjectSummary? project = projectId is { } id ? await projectDirectory.GetProjectAsync(id, ct) : null;
        if (project is null || project.ClientId != clientId.Value)
        {
            return Result.Failure<BriefHeadDiffResponse>(CrmPortalErrors.NotFound);
        }

        return await BriefRoundDiffs.BuildAsync(db, query.BriefId, query.Round, BriefDiffAudience.Client, ct);
    }
}

internal static class PortalClients
{
    /// <summary>The calling client's id — <c>CrmScopeDecorator</c> only gates, it never passes the actor
    /// through, so each portal handler re-resolves it.</summary>
    public static async Task<Result<Guid>> ResolveClientIdAsync(ICrmAccessGuard guard, string appUserId, CancellationToken ct)
    {
        Result<CrmActor> actor = await guard.ResolveActorAsync(appUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<Guid>(actor.Error);
        }

        return actor.Value!.Kind == CrmActorKind.Client && actor.Value.ClientId is { } clientId
            ? clientId
            : Result.Failure<Guid>(CrmPortalErrors.NotAClient);
    }
}
