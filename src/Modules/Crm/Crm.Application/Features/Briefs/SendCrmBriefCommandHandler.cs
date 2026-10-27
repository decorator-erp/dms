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
// Approvals.Contracts.ApprovalType and Projects.Contracts.ApprovalType are two independent full
// duplicates of the same enum (Projects.Contracts' own remarks: "Contracts cannot reference another
// module's Contracts") - Crm.Application references both modules' Contracts, so the plain name is
// ambiguous. This handler always means the Approvals one.
using ApprovalType = Approvals.Contracts.ApprovalType;

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
/// lands on the same approval: <c>RequestAsync</c> returns it, with the decision if the client already
/// made one in between.</para>
///
/// <para><b>A missed decision is not always safe to apply.</b> That orphaned approval was decided while
/// its round's snapshot had rolled back, so the client decided on the previous round's head (or on
/// nothing), and the office may have edited the head since. A missed Rejected/ChangesRequested is applied
/// — sending the head back for rework is right whatever the client saw. A missed <b>Approved</b> is not:
/// the round is voided (its snapshot stays as the record; the approval stays decided in Approvals) and
/// the head goes out again as round N+1 under a fresh deterministic id, so only an approval of content
/// the client was actually shown can approve the brief.</para>
///
/// <para><b>Recovery.</b> Sending a brief that is already PendingApproval re-requests its own
/// <see cref="CrmDesignBrief.PendingApprovalId"/> when Approvals has no such approval; otherwise it is
/// <see cref="CrmBriefErrors.NotSendable"/>.</para>
/// </summary>
internal sealed class SendCrmBriefCommandHandler(
    CrmDbContext db, IProjectDirectory projectDirectory, IApprovalRequester approvalRequester, CrmApprovalOutcomes outcomes)
    : ICommandHandler<SendCrmBriefCommand>
{
    /// <summary>Each voided round needs a CRM commit to have failed after its approval committed; more than
    /// a couple in a row means something else is wrong, so the send stops instead of looping.</summary>
    private const int MaxVoidedRounds = 3;

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

        for (int voided = 0; ; voided++)
        {
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

            if (requestResult.Value!.Decision is not { } missedDecision)
            {
                break;
            }

            if (missedDecision != ApprovalDecision.Approved)
            {
                // ApprovalRequestOutcome carries no decision time, so the round's DecidedAtUtc is now:
                // approximate, in this rare path only.
                Result applied = await outcomes.ApplyToBriefAsync(brief, approvalId, missedDecision, DateTime.UtcNow, ct);
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

            // Approved on content the client never saw: void this round, send the head as the next one.
            int nextRound = await db.CrmBriefHeadSnapshots.CountAsync(s => s.BriefId == brief.Id, ct) + 1;
            Guid nextApprovalId = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, brief.Id, nextRound);
            Result voidResult = brief.VoidRound(approvalId, nextApprovalId);
            if (voidResult.IsFailure)
            {
                return voidResult;
            }

            // The voided round is the one before nextRound, marked by its number before nextRound exists.
            CrmBriefHeadSnapshot? voidedRound = await db.CrmBriefHeadSnapshots
                .FirstOrDefaultAsync(s => s.BriefId == brief.Id && s.Round == nextRound - 1, ct);
            voidedRound?.RecordOutcome(BriefRoundOutcome.Voided, DateTime.UtcNow);

            db.CrmBriefHeadSnapshots.Add(CrmBriefHeadSnapshot.Create(brief, nextRound));
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
