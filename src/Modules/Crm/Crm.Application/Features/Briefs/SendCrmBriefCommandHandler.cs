using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
// Approvals.Contracts.ApprovalType and Projects.Contracts.ApprovalType are two independent full
// duplicates of the same enum (Projects.Contracts' own remarks: "Contracts cannot reference another
// module's Contracts") - Crm.Application references both modules' Contracts, so the plain name is
// ambiguous. This handler always means the Approvals one.
using ApprovalType = Approvals.Contracts.ApprovalType;

namespace Crm.Application.Features.Briefs;

internal sealed class SendCrmBriefCommandValidator : AbstractValidator<SendCrmBriefCommand>
{
    public SendCrmBriefCommandValidator() => RuleFor(x => x.BriefId).NotEmpty();
}

/// <summary>
/// Sends the head for CLIENT approval — captures a <c>CrmBriefHeadSnapshot</c> round (FR-029's diff
/// source) and requests an <see cref="ApprovalSubjectType.DesignBrief"/> decision via
/// <see cref="IApprovalRequester"/>, approver <see cref="ApproverKind.Client"/>. The approver is
/// resolved from the brief's own project → <see cref="IProjectDirectory"/>'s <c>ClientId</c> → this
/// module's own <c>crm.clients</c> row for that id's <c>AppUserId</c> — a same-schema lookup, not a
/// second cross-module call, since CRM owns the client row directly.
/// </summary>
internal sealed class SendCrmBriefCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory, IApprovalRequester approvalRequester)
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

        int round = await db.CrmBriefHeadSnapshots.CountAsync(s => s.BriefId == brief.Id, ct) + 1;
        db.CrmBriefHeadSnapshots.Add(CrmBriefHeadSnapshot.Create(brief, round));

        Result pendingResult = brief.MarkPendingApproval();
        if (pendingResult.IsFailure)
        {
            return pendingResult;
        }

        Result<Guid> requestResult = await approvalRequester.RequestAsync(
            new ApprovalRequest(
                ApprovalSubjectType.DesignBrief, brief.Id, ApprovalType.ClientReview,
                RequestedByUserId: client.AppUserId,
                [new ApproverSpec(ApproverKind.Client, client.AppUserId)],
                DueAtUtc: null),
            ct);
        if (requestResult.IsFailure)
        {
            return requestResult;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
