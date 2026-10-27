using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class UpdateCrmRequirementItemCommandValidator : AbstractValidator<UpdateCrmRequirementItemCommand>
{
    public UpdateCrmRequirementItemCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Room).MaximumLength(128);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.SourceType).IsInEnum();
    }
}

internal sealed class UpdateCrmRequirementItemCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<UpdateCrmRequirementItemCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        CrmRequirementItem? item = await db.CrmRequirementItems.FindAsync([command.ItemId], ct);
        if (item is null)
        {
            return CrmItemErrors.NotFound;
        }

        Result open = await ItemReferenceValidator.EnsurePeriodOpenAsync(db, item.PeriodId, ct);
        if (open.IsFailure)
        {
            return open;
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(item.ProjectId, ct);
        if (project is null)
        {
            return CrmItemErrors.NotFound;
        }

        Result spaceType = await ItemReferenceValidator.ValidateSpaceTypeAsync(db, command.SpaceTypeId, ct);
        if (spaceType.IsFailure)
        {
            return spaceType;
        }

        Result references = await ItemReferenceValidator.ValidateAsync(
            db, projectDirectory, project, command.ProjectTaskId, command.MeetingId, command.BriefId, command.NoteId, ct);
        if (references.IsFailure)
        {
            return references;
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
