using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class SupersedeCrmBriefSpaceCommandValidator : AbstractValidator<SupersedeCrmBriefSpaceCommand>
{
    public SupersedeCrmBriefSpaceCommandValidator()
    {
        RuleFor(x => x.OriginalEntryId).NotEmpty();
        RuleFor(x => x.CreatedById).NotEmpty();
    }
}

internal sealed class SupersedeCrmBriefSpaceCommandHandler(CrmDbContext db)
    : ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SupersedeCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        CrmBriefSpace? original = await db.CrmBriefSpaces.FindAsync([command.OriginalEntryId], ct);
        if (original is null)
        {
            return Result.Failure<Guid>(CrmBriefErrors.NotFound);
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
        await db.SaveChangesAsync(ct);

        return superseding.Id;
    }
}
