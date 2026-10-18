using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class UpdateCrmBriefSpaceCommandValidator : AbstractValidator<UpdateCrmBriefSpaceCommand>
{
    public UpdateCrmBriefSpaceCommandValidator()
    {
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        // Requirements matches CrmBriefSpaceConfiguration's column length.
        RuleFor(x => x.Requirements).MaximumLength(2000);
        RuleFor(x => x.CountOrPieces).GreaterThanOrEqualTo(1).When(x => x.CountOrPieces.HasValue);
    }
}

internal sealed class UpdateCrmBriefSpaceCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<UpdateCrmBriefSpaceCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        CrmBriefSpace? entry = await db.CrmBriefSpaces.FindAsync([command.EntryId], ct);
        if (entry is null)
        {
            return CrmBriefErrors.NotFound;
        }

        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == entry.BriefId, ct);
        if (brief is null)
        {
            return CrmBriefErrors.NotFound;
        }

        Result references = await BriefReferenceValidator.ValidateEntryReferencesAsync(
            db, projectDirectory, brief, command.SpaceId, command.ProjectTaskId, command.MeetingId, ct);
        if (references.IsFailure)
        {
            return references;
        }

        Result result = entry.Update(
            command.SpaceId, command.CountOrPieces, command.Requirements, command.MeetingId, command.ProjectTaskId);
        if (result.IsFailure)
        {
            return result;
        }

        return await BriefWrites.SaveAsync(db, ct);
    }
}
