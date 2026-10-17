using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
// Same ambiguity as SendCrmBriefCommandHandler - see that file's identical comment.
using ApprovalType = Approvals.Contracts.ApprovalType;

namespace Crm.Application.Features.Briefs;

internal sealed class SendCrmBriefSpaceCommandValidator : AbstractValidator<SendCrmBriefSpaceCommand>
{
    public SendCrmBriefSpaceCommandValidator() => RuleFor(x => x.EntryId).NotEmpty();
}

/// <summary>Same client-approver resolution shape as <c>SendCrmBriefCommandHandler</c> — via the
/// entry's parent brief's project.</summary>
internal sealed class SendCrmBriefSpaceCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory, IApprovalRequester approvalRequester)
    : ICommandHandler<SendCrmBriefSpaceCommand>
{
    public async Task<Result> HandleAsync(SendCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        CrmBriefSpace? entry = await db.CrmBriefSpaces.FirstOrDefaultAsync(e => e.Id == command.EntryId, ct);
        if (entry is null)
        {
            return CrmBriefErrors.NotFound;
        }

        Result sendResult = entry.Send();
        if (sendResult.IsFailure)
        {
            return sendResult;
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

        Result<Guid> requestResult = await approvalRequester.RequestAsync(
            new ApprovalRequest(
                ApprovalSubjectType.BriefSpace, entry.Id, ApprovalType.ClientReview,
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
