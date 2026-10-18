using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
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

/// <summary>Same client-approver resolution shape as <c>SendCrmBriefCommandHandler</c> (G9-10) — via
/// the item's own project, since a requirement item, unlike a brief entry, has <c>ProjectId</c>
/// directly rather than reaching it through a parent.</summary>
internal sealed class SendCrmRequirementItemCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory, IApprovalRequester approvalRequester)
    : ICommandHandler<SendCrmRequirementItemCommand>
{
    public async Task<Result> HandleAsync(SendCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        CrmRequirementItem? item = await db.CrmRequirementItems.FirstOrDefaultAsync(i => i.Id == command.ItemId, ct);
        if (item is null)
        {
            return CrmItemErrors.NotFound;
        }

        Result sendResult = item.Send();
        if (sendResult.IsFailure)
        {
            return sendResult;
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(item.ProjectId, ct);
        if (project is null)
        {
            return CrmItemErrors.NotFound;
        }

        Client? client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == project.ClientId, ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        Result<ApprovalRequestOutcome> requestResult = await approvalRequester.RequestAsync(
            new ApprovalRequest(
                Guid.NewGuid(), ApprovalSubjectType.RequirementItem, item.Id, ApprovalType.ClientReview,
                RequestedByUserId: command.SentByUserId,
                [new ApproverSpec(ApproverKind.Client, client.AppUserId)],
                DueAtUtc: null),
            ct);
        if (requestResult.IsFailure)
        {
            return requestResult.Error;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
