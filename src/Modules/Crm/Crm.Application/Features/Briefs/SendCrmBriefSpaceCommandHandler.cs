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
// Crm.Domain's own ApprovalState (the brief's status) vs Approvals.Contracts' - this file means CRM's.
using ApprovalState = Crm.Domain.ApprovalState;
// Same ambiguity as SendCrmBriefCommandHandler - see that file's identical comment.
using ApprovalType = Approvals.Contracts.ApprovalType;

namespace Crm.Application.Features.Briefs;

internal sealed class SendCrmBriefSpaceCommandValidator : AbstractValidator<SendCrmBriefSpaceCommand>
{
    public SendCrmBriefSpaceCommandValidator()
    {
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.SentByUserId).NotEmpty();
    }
}

/// <summary>Same client-approver resolution, flush-then-request order, deterministic round id, recovery
/// rule and missed-decision rule (Rejected applied, Approved voided and re-sent) as
/// <c>SendCrmBriefCommandHandler</c> — via the entry's parent brief's project, with the entry's own
/// <see cref="CrmBriefSpace.SendCount"/> as its round.</summary>
internal sealed class SendCrmBriefSpaceCommandHandler(
    CrmDbContext db, IProjectDirectory projectDirectory, IApprovalRequester approvalRequester, CrmApprovalOutcomes outcomes)
    : ICommandHandler<SendCrmBriefSpaceCommand>
{
    /// <summary>See <c>SendCrmBriefCommandHandler.MaxVoidedRounds</c>.</summary>
    private const int MaxVoidedRounds = 3;

    public async Task<Result> HandleAsync(SendCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        CrmBriefSpace? entry = await db.CrmBriefSpaces.FirstOrDefaultAsync(e => e.Id == command.EntryId, ct);
        if (entry is null)
        {
            return CrmBriefErrors.NotFound;
        }

        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == entry.BriefId, ct);
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
        if (entry.ApprovalStatus == ApprovalState.PendingApproval)
        {
            // D-10 (A-F1): a legacy pending entry with no bound id adopts the entry's newest approval, then
            // follows the A-11 rule below like any other. L-1: only an undecided one or the entry's own last
            // send; otherwise (none, or an older round's decision) it is re-requested under a fresh round id.
            if (entry.PendingApprovalId is null)
            {
                ApprovalSnapshot? latest = await approvalRequester.GetLatestForSubjectAsync(
                    ApprovalSubjectType.BriefSpace, entry.Id, ct);
                bool adopt = CrmApprovalIds.IsAdoptable(latest, ApprovalSubjectType.BriefSpace, entry.Id, entry.SendCount);
                Result adopted = entry.AdoptPendingApproval(
                    adopt ? latest!.Id : CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entry.Id, entry.SendCount + 1),
                    newSend: !adopt);
                if (adopted.IsFailure)
                {
                    return adopted;
                }

                Result saveAdopted = await BriefWrites.SaveAsync(db, ct);
                if (saveAdopted.IsFailure)
                {
                    return saveAdopted;
                }
            }

            // A-11 (B-L1): only a live, UNDECIDED approval blocks a resend. A missing one (never committed)
            // or a decided one whose event was lost is re-requested under the same id: RequestAsync returns
            // its decision, and the loop below applies a rejection or voids an approval.
            Guid pendingId = entry.PendingApprovalId!.Value;
            if (await approvalRequester.GetAsync(pendingId, ct) is { Decision: null })
            {
                return CrmBriefErrors.EntryPending;
            }

            approvalId = pendingId;
        }
        else
        {
            approvalId = CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entry.Id, entry.SendCount + 1);

            Result sendResult = entry.Send(approvalId);
            if (sendResult.IsFailure)
            {
                return sendResult;
            }

            Result saveResult = await BriefWrites.SaveAsync(db, ct);
            if (saveResult.IsFailure)
            {
                return saveResult;
            }
        }

        for (int voided = 0; ; voided++)
        {
            Result<ApprovalRequestOutcome> requestResult = await approvalRequester.RequestAsync(
                new ApprovalRequest(
                    approvalId, ApprovalSubjectType.BriefSpace, entry.Id, ApprovalType.ClientReview,
                    RequestedByUserId: command.SentByUserId,
                    [new ApproverSpec(ApproverKind.Client, client.AppUserId)],
                    DueAtUtc: null),
                ct);
            if (requestResult.IsFailure)
            {
                return requestResult.Error;
            }

            if (requestResult.Value!.Decision is not { } missedDecision)
            {
                break;
            }

            if (missedDecision != ApprovalDecision.Approved)
            {
                Result applied = await outcomes.ApplyToBriefSpaceAsync(entry, approvalId, missedDecision, ct);
                if (applied.IsFailure)
                {
                    return applied;
                }

                break;
            }

            if (voided == MaxVoidedRounds)
            {
                return CrmBriefErrors.TooManyVoidedRounds;
            }

            // Approved on content the client never saw: void this send, send the entry again.
            Guid nextApprovalId = CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entry.Id, entry.SendCount + 1);
            Result voidResult = entry.VoidSend(approvalId, nextApprovalId);
            if (voidResult.IsFailure)
            {
                return voidResult;
            }

            Result saveNext = await BriefWrites.SaveAsync(db, ct);
            if (saveNext.IsFailure)
            {
                return saveNext;
            }

            approvalId = nextApprovalId;
        }

        return await BriefWrites.SaveAsync(db, ct);
    }
}
