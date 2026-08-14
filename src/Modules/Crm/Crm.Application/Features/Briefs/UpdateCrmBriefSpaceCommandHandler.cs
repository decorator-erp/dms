using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class UpdateCrmBriefSpaceCommandValidator : AbstractValidator<UpdateCrmBriefSpaceCommand>
{
    public UpdateCrmBriefSpaceCommandValidator() => RuleFor(x => x.EntryId).NotEmpty();
}

internal sealed class UpdateCrmBriefSpaceCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmBriefSpaceCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmBriefSpaceCommand command, CancellationToken ct = default)
    {
        var entry = await db.CrmBriefSpaces.FindAsync([command.EntryId], ct);
        if (entry is null)
            return CrmBriefErrors.NotFound;

        var result = entry.Update(
            command.SpaceId, command.CountOrPieces, command.Requirements, command.MeetingId, command.ProjectTaskId);
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
