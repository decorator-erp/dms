using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
// Crm.Domain's own ApprovalState (the item's status) vs Approvals.Contracts' - this file means CRM's.
using ApprovalState = Crm.Domain.ApprovalState;
// Same Approvals.Contracts.ApprovalType / Projects.Contracts.ApprovalType ambiguity as
// SendCrmBriefCommandHandler (G9-10) - see that file's identical comment.
using ApprovalType = Approvals.Contracts.ApprovalType;

namespace Crm.Application.Features.Items;

internal sealed class SendCrmRequirementItemCommandValidator : AbstractValidator<SendCrmRequirementItemCommand>
{
    public SendCrmRequirementItemCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.SentByUserId).NotEmpty();
    }
}

/// <summary>A-10 (B-M7): the brief-space send pattern for requirement items — same client-approver
/// resolution (via the item's own project), flush-then-request order, deterministic round id
/// (<see cref="CrmApprovalIds"/> over <see cref="CrmRequirementItem.SendCount"/>), recovery rule (a
/// Pending item whose approval never committed is re-requested under the same id) and missed-decision
/// rule (Rejected applied; Approved voided and re-sent, since the client approved content they never
/// saw). Saves go through <see cref="ItemWrites"/>, so a concurrent send is a 409.</summary>
internal sealed class SendCrmRequirementItemCommandHandler(
    CrmDbContext db, IProjectDirectory projectDirectory, IApprovalRequester approvalRequester, CrmApprovalOutcomes outcomes)
    : ICommandHandler<SendCrmRequirementItemCommand>
{
    /// <summary>See <c>SendCrmBriefCommandHandler.MaxVoidedRounds</c>.</summary>
    private const int MaxVoidedRounds = 3;

    public async Task<Result> HandleAsync(SendCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        CrmRequirementItem? item = await db.CrmRequirementItems.FirstOrDefaultAsync(i => i.Id == command.ItemId, ct);
        if (item is null)
        {
            return CrmItemErrors.NotFound;
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(item.ProjectId, ct);
        if (project is null)
        {
            return CrmItemErrors.NotFound;
        }

        Result active = ItemReferenceValidator.EnsureProjectActive(project);
        if (active.IsFailure)
        {
            return active;
        }

        Client? client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == project.ClientId, ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        Guid approvalId;
        if (item.ApprovalStatus == ApprovalState.PendingApproval)
        {
            // A-11 (B-L1): only a live, UNDECIDED approval blocks a resend. A missing one (never committed)
            // or a decided one whose event was lost is re-requested under the same id: RequestAsync returns
            // its decision, and the loop below applies a rejection or voids an approval.
            if (item.PendingApprovalId is not { } pendingId
                || await approvalRequester.GetAsync(pendingId, ct) is { Decision: null })
            {
                return CrmItemErrors.AlreadyPending;
            }

            approvalId = pendingId;
        }
        else
        {
            approvalId = CrmApprovalIds.For(ApprovalSubjectType.RequirementItem, item.Id, item.SendCount + 1);

            Result sendResult = item.Send(approvalId);
            if (sendResult.IsFailure)
            {
                return sendResult;
            }

            Result saveResult = await ItemWrites.SaveAsync(db, ct);
            if (saveResult.IsFailure)
            {
                return saveResult;
            }
        }

        for (int voided = 0; ; voided++)
        {
            Result<ApprovalRequestOutcome> requestResult = await approvalRequester.RequestAsync(
                new ApprovalRequest(
                    approvalId, ApprovalSubjectType.RequirementItem, item.Id, ApprovalType.ClientReview,
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
                return await outcomes.ApplyToItemAsync(item, approvalId, missedDecision, ct);
            }

            if (voided == MaxVoidedRounds)
            {
                return CrmItemErrors.TooManyVoidedRounds;
            }

            // Approved on content the client never saw: void this send, send the item again.
            Guid nextApprovalId = CrmApprovalIds.For(ApprovalSubjectType.RequirementItem, item.Id, item.SendCount + 1);
            Result voidResult = item.VoidSend(approvalId, nextApprovalId);
            if (voidResult.IsFailure)
            {
                return voidResult;
            }

            Result saveNext = await ItemWrites.SaveAsync(db, ct);
            if (saveNext.IsFailure)
            {
                return saveNext;
            }

            approvalId = nextApprovalId;
        }

        return await ItemWrites.SaveAsync(db, ct);
    }
}
