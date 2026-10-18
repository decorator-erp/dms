using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class CreateCrmBriefSpaceCommandValidator : AbstractValidator<CreateCrmBriefSpaceCommand>
{
    public CreateCrmBriefSpaceCommandValidator()
    {
        RuleFor(x => x.BriefId).NotEmpty();
        RuleFor(x => x.CreatedById).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        // Requirements matches CrmBriefSpaceConfiguration's column length.
        RuleFor(x => x.Requirements).MaximumLength(2000);
        RuleFor(x => x.CountOrPieces).GreaterThanOrEqualTo(1).When(x => x.CountOrPieces.HasValue);
    }
}

internal sealed class CreateCrmBriefSpaceCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateCrmBriefSpaceCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == command.BriefId, ct);
        if (brief is null)
        {
            return Result.Failure<Guid>(CrmBriefErrors.NotFound);
        }

        Result references = await BriefReferenceValidator.ValidateEntryReferencesAsync(
            db, projectDirectory, brief, command.SpaceId, command.ProjectTaskId, command.MeetingId, ct);
        if (references.IsFailure)
        {
            return Result.Failure<Guid>(references.Error);
        }

        Result<CrmBriefSpace> entryResult = CrmBriefSpace.Create(
            command.BriefId, command.SpaceId, command.CreatedById, command.CountOrPieces,
            command.Requirements, command.MeetingId, command.ProjectTaskId);
        if (entryResult.IsFailure)
        {
            return Result.Failure<Guid>(entryResult.Error);
        }

        CrmBriefSpace entry = entryResult.Value!;
        db.CrmBriefSpaces.Add(entry);
        Result saved = await BriefWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }

        return entry.Id;
    }
}
