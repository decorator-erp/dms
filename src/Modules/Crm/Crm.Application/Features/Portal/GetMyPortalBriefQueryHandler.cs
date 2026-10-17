using BuildingBlocks.Messaging;
using Crm.Contracts;
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
    : IQueryHandler<GetMyPortalBriefQuery, PortalBriefResponse>
{
    public async Task<Result<PortalBriefResponse>> HandleAsync(GetMyPortalBriefQuery query, CancellationToken ct = default)
    {
        Result<CrmActor> actor = await guard.ResolveActorAsync(query.AppUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<PortalBriefResponse>(actor.Error);
        }

        if (actor.Value!.Kind != CrmActorKind.Client)
        {
            return Result.Failure<PortalBriefResponse>(CrmPortalErrors.NotAClient);
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(query.ProjectId, ct);
        if (project is null || project.ClientId != actor.Value.ClientId)
        {
            return Result.Failure<PortalBriefResponse>(CrmPortalErrors.NotFound);
        }

        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.ProjectId == query.ProjectId, ct);
        if (brief is null)
        {
            return Result.Failure<PortalBriefResponse>(CrmPortalErrors.NotFound);
        }

        return new PortalBriefResponse(brief.Id, brief.ProjectId, CrmEnumMapping.ToContracts(brief.Status));
    }
}
