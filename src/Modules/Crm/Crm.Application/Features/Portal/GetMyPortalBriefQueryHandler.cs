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
/// <para><c>GetMyPortalMeetingsQuery</c> and <c>DecideMyPortalApprovalCommand</c> — the other two
/// Portal operations <c>Crm.Contracts</c> declares — are deliberately NOT implemented at this task.
/// <c>GetMyPortalMeetingsQuery</c>'s own doc comment says it resolves "the calling client's own
/// projects," but <c>Projects.Contracts.IProjectDirectory</c> has no client-scoped project lookup (only
/// by-id), and no Meeting-creation command was ever built in this phase (Meetings' Application layer
/// was named in the plan's "complete spine" but never actually implemented in G9-09→G9-12, a real gap
/// recorded in this task's PROGRESS.md entry) — so the query would only ever return an empty list, and
/// closing the underlying gap means either a new cross-module contract method or a CRM-local
/// reinterpretation, either of which is an architectural decision this endpoints task shouldn't make
/// unilaterally. <c>DecideMyPortalApprovalCommand</c> would route through <c>Approvals.Contracts</c>,
/// whose <c>approval_approvers</c> ledger is employee-scoped in this port — giving a client actor a
/// decision path there is the same kind of design question, not an endpoint-wiring one. Both are
/// deferred explicitly, not silently.</para>
/// </summary>
internal sealed class GetMyPortalBriefQueryHandler(CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory)
    : IQueryHandler<GetMyPortalBriefQuery, PortalBriefResponse>
{
    public async Task<Result<PortalBriefResponse>> HandleAsync(GetMyPortalBriefQuery query, CancellationToken ct = default)
    {
        var actor = await guard.ResolveActorAsync(query.AppUserId, ct);
        if (actor.IsFailure)
            return Result.Failure<PortalBriefResponse>(actor.Error);
        if (actor.Value!.Kind != CrmActorKind.Client)
            return Result.Failure<PortalBriefResponse>(CrmPortalErrors.NotAClient);

        var project = await projectDirectory.GetProjectAsync(query.ProjectId, ct);
        if (project is null || project.ClientId != actor.Value.ClientId)
            return Result.Failure<PortalBriefResponse>(CrmPortalErrors.NotFound);

        var brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.ProjectId == query.ProjectId, ct);
        if (brief is null)
            return Result.Failure<PortalBriefResponse>(CrmPortalErrors.NotFound);

        return new PortalBriefResponse(brief.Id, brief.ProjectId, CrmEnumMapping.ToContracts(brief.Status));
    }
}
