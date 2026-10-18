using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
// Approvals.Contracts.ApprovalType and Projects.Contracts.ApprovalType are two independent full
// duplicates of the same enum (Projects.Contracts' own remarks: "Contracts cannot reference another
// module's Contracts") - Crm.Application references both modules' Contracts, so the plain name is
// ambiguous. This handler always means the Approvals one.
using ApprovalType = Approvals.Contracts.ApprovalType;
// Crm.Domain's own ApprovalState (the brief's status) vs Approvals.Contracts' - this file means CRM's.
using ApprovalState = Crm.Domain.ApprovalState;

namespace Crm.Application.Features.Briefs;

internal sealed class SendCrmBriefCommandValidator : AbstractValidator<SendCrmBriefCommand>
{
    public SendCrmBriefCommandValidator()
    {
        RuleFor(x => x.BriefId).NotEmpty();
        RuleFor(x => x.SentByUserId).NotEmpty();
    }
}

/// <summary>
/// Sends the head for CLIENT approval — captures a <c>CrmBriefHeadSnapshot</c> round (FR-029's diff
/// source) and requests an <see cref="ApprovalSubjectType.DesignBrief"/> decision via
/// <see cref="IApprovalRequester"/>, approver <see cref="ApproverKind.Client"/>, requester the office user
/// who sent it (<see cref="SendCrmBriefCommand.SentByUserId"/>). The approver is resolved from the
/// brief's own project → <see cref="IProjectDirectory"/>'s <c>ClientId</c> → this module's own
/// <c>crm.clients</c> row for that id's <c>AppUserId</c>.
///
/// <para><b>Order matters.</b> This handler runs inside the command pipeline's CRM transaction, while
/// <see cref="IApprovalRequester.RequestAsync"/> commits in Approvals' own. So CRM flushes first —
/// snapshot, <c>BeginRound</c>, <see cref="BriefWrites.SaveAsync"/> — and only then requests: a second
/// concurrent send collides on the unique <c>(BriefId, Round)</c> index or the brief's <c>xmin</c> right
/// there (409, nothing requested), and a failed request returns its error, which rolls the whole CRM
/// transaction back (no snapshot, prior status). The approval id is deterministic per round
/// (<see cref="CrmApprovalIds"/>), so if CRM's commit fails AFTER the approval committed, the retry
/// lands on the same approval: <c>RequestAsync</c> returns it, and if the client already decided it in
/// between, the decision is applied here through <see cref="CrmApprovalOutcomes"/>.</para>
///
/// <para><b>Recovery.</b> Sending a brief that is already PendingApproval re-requests its own
/// <see cref="CrmDesignBrief.PendingApprovalId"/> when Approvals has no such approval; otherwise it is
/// <see cref="CrmBriefErrors.NotSendable"/>.</para>
/// </summary>
internal sealed class SendCrmBriefCommandHandler(
    CrmDbContext db, IProjectDirectory projectDirectory, IApprovalRequester approvalRequester, CrmApprovalOutcomes outcomes)
    : ICommandHandler<SendCrmBriefCommand>
{
    public async Task<Result> HandleAsync(SendCrmBriefCommand command, CancellationToken ct = default)
    {
        CrmDesignBrief? brief = await db.CrmDesignBriefs.FirstOrDefaultAsync(b => b.Id == command.BriefId, ct);
        if (brief is null)
        {
            return CrmBriefErrors.NotFound;
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(brief.ProjectId, ct);
        if (project is null)
        {
            return CrmBriefErrors.NotFound;
        }

        Client? client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == project.ClientId, ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        Guid approvalId;
        if (brief.Status == ApprovalState.PendingApproval)
        {
            if (brief.PendingApprovalId is not { } pendingId
                || await approvalRequester.GetAsync(pendingId, ct) is not null)
            {
                return CrmBriefErrors.NotSendable;
            }

            approvalId = pendingId;
        }
        else
        {
            int round = await db.CrmBriefHeadSnapshots.CountAsync(s => s.BriefId == brief.Id, ct) + 1;
            approvalId = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, brief.Id, round);

            Result beginResult = brief.BeginRound(approvalId);
            if (beginResult.IsFailure)
            {
                return beginResult;
            }

            db.CrmBriefHeadSnapshots.Add(CrmBriefHeadSnapshot.Create(brief, round));

            Result saveResult = await BriefWrites.SaveAsync(db, ct);
            if (saveResult.IsFailure)
            {
                return saveResult;
            }
        }

        Result<ApprovalRequestOutcome> requestResult = await approvalRequester.RequestAsync(
            new ApprovalRequest(
                approvalId, ApprovalSubjectType.DesignBrief, brief.Id, ApprovalType.ClientReview,
                RequestedByUserId: command.SentByUserId,
                [new ApproverSpec(ApproverKind.Client, client.AppUserId)],
                DueAtUtc: null),
            ct);
        if (requestResult.IsFailure)
        {
            return requestResult.Error;
        }

        if (requestResult.Value!.Decision is { } missedDecision)
        {
            Result applied = outcomes.ApplyToBrief(brief, approvalId, missedDecision);
            if (applied.IsFailure)
            {
                return applied;
            }
        }

        return await BriefWrites.SaveAsync(db, ct);
    }
}
