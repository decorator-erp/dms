using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class UpdateCrmRequirementItemCommandValidator : AbstractValidator<UpdateCrmRequirementItemCommand>
{
    public UpdateCrmRequirementItemCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty();
    }
}

internal sealed class UpdateCrmRequirementItemCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmRequirementItemCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        CrmRequirementItem? item = await db.CrmRequirementItems.FindAsync([command.ItemId], ct);
        if (item is null)
        {
            return CrmItemErrors.NotFound;
        }

        Result result = item.Update(
            CrmEnumMapping.ToDomain(command.Category), command.Title, CrmEnumMapping.ToDomain(command.Priority),
            command.SpaceTypeId, command.Room, command.Description,
            command.SourceType is { } st ? CrmEnumMapping.ToDomain(st) : null,
            command.MeetingId, command.BriefId, command.NoteId, command.ProjectTaskId);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
