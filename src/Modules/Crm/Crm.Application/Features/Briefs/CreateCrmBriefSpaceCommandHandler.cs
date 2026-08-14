using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class CreateCrmBriefSpaceCommandValidator : AbstractValidator<CreateCrmBriefSpaceCommand>
{
    public CreateCrmBriefSpaceCommandValidator()
    {
        RuleFor(x => x.BriefId).NotEmpty();
        RuleFor(x => x.CreatedById).NotEmpty();
    }
}

internal sealed class CreateCrmBriefSpaceCommandHandler(CrmDbContext db) : ICommandHandler<CreateCrmBriefSpaceCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        var briefExists = await db.CrmDesignBriefs.AnyAsync(b => b.Id == command.BriefId, ct);
        if (!briefExists)
            return Result.Failure<Guid>(CrmBriefErrors.NotFound);

        var entryResult = CrmBriefSpace.Create(
            command.BriefId, command.SpaceId, command.CreatedById, command.CountOrPieces,
            command.Requirements, command.MeetingId, command.ProjectTaskId);
        if (entryResult.IsFailure)
            return Result.Failure<Guid>(entryResult.Error);

        var entry = entryResult.Value!;
        db.CrmBriefSpaces.Add(entry);
        await db.SaveChangesAsync(ct);

        return entry.Id;
    }
}
