using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class SupersedeCrmBriefSpaceCommandValidator : AbstractValidator<SupersedeCrmBriefSpaceCommand>
{
    public SupersedeCrmBriefSpaceCommandValidator()
    {
        RuleFor(x => x.OriginalEntryId).NotEmpty();
        RuleFor(x => x.CreatedById).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        // Requirements matches CrmBriefSpaceConfiguration's column length.
        RuleFor(x => x.Requirements).MaximumLength(2000);
        RuleFor(x => x.CountOrPieces).GreaterThanOrEqualTo(1).When(x => x.CountOrPieces.HasValue);
    }
}

/// <summary>The ledger cannot fork: an entry is superseded at most once — checked here (409
/// <see cref="CrmBriefErrors.AlreadySuperseded"/>) and backed by the partial unique index on
/// <c>SupersedesSpaceId</c>, whose violation <see cref="BriefWrites"/> maps to the same error.</summary>
internal sealed class SupersedeCrmBriefSpaceCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SupersedeCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        CrmBriefSpace? original = await db.CrmBriefSpaces.FindAsync([command.OriginalEntryId], ct);
        if (original is null)
        {
            return Result.Failure<Guid>(CrmBriefErrors.NotFound);
        }

        if (await db.CrmBriefSpaces.AnyAsync(e => e.SupersedesSpaceId == original.Id, ct))
        {
            return Result.Failure<Guid>(CrmBriefErrors.AlreadySuperseded);
        }

        CrmDesignBrief? brief = await db.CrmDesignBriefs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == original.BriefId, ct);
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

        Result<CrmBriefSpace> supersedingResult = CrmBriefSpace.CreateSuperseding(
            original, command.SpaceId, command.CreatedById, command.CountOrPieces,
            command.Requirements, command.MeetingId, command.ProjectTaskId);
        if (supersedingResult.IsFailure)
        {
            return Result.Failure<Guid>(supersedingResult.Error);
        }

        CrmBriefSpace superseding = supersedingResult.Value!;
        db.CrmBriefSpaces.Add(superseding);
        Result saved = await BriefWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }

        return superseding.Id;
    }
}
