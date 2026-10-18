using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class UpdateCrmBriefHeadCommandValidator : AbstractValidator<UpdateCrmBriefHeadCommand>
{
    public UpdateCrmBriefHeadCommandValidator()
    {
        RuleFor(x => x.BriefId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

internal sealed class UpdateCrmBriefHeadCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<UpdateCrmBriefHeadCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmBriefHeadCommand command, CancellationToken ct = default)
    {
        CrmDesignBrief? brief = await db.CrmDesignBriefs.FindAsync([command.BriefId], ct);
        if (brief is null)
        {
            return CrmBriefErrors.NotFound;
        }

        // LastMeetingId is the same kind of reference as an entry's MeetingId: it must be this project's.
        Result references = await BriefReferenceValidator.ValidateEntryReferencesAsync(
            db, projectDirectory, brief, spaceId: null, projectTaskId: null, command.LastMeetingId, ct);
        if (references.IsFailure)
        {
            return references;
        }

        Result result = brief.UpdateHead(BriefMapping.ToDomain(command.Data), command.LastMeetingId);
        if (result.IsFailure)
        {
            return result;
        }

        return await BriefWrites.SaveAsync(db, ct);
    }
}
