using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class SupersedeCrmRequirementItemCommandValidator : AbstractValidator<SupersedeCrmRequirementItemCommand>
{
    public SupersedeCrmRequirementItemCommandValidator()
    {
        RuleFor(x => x.OriginalItemId).NotEmpty();
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty();
    }
}

internal sealed class SupersedeCrmRequirementItemCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<SupersedeCrmRequirementItemCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SupersedeCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        CrmRequirementItem? original = await db.CrmRequirementItems.FindAsync([command.OriginalItemId], ct);
        if (original is null)
        {
            return Result.Failure<Guid>(CrmItemErrors.NotFound);
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(original.ProjectId, ct);
        if (project is null)
        {
            return Result.Failure<Guid>(CrmItemErrors.NotFound);
        }

        Result references = await ItemReferenceValidator.ValidateAsync(
            db, projectDirectory, project, command.ProjectTaskId, command.MeetingId, command.BriefId, command.NoteId, ct);
        if (references.IsFailure)
        {
            return Result.Failure<Guid>(references.Error);
        }

        Result<CrmRequirementItem> result = CrmRequirementItem.CreateSuperseding(
            original, command.EmployeeId, CrmEnumMapping.ToDomain(command.Category), command.Title,
            CrmEnumMapping.ToDomain(command.Priority), command.SpaceTypeId, command.Room, command.Description,
            command.SourceType is { } st ? CrmEnumMapping.ToDomain(st) : null,
            command.MeetingId, command.BriefId, command.NoteId, command.ProjectTaskId);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmRequirementItem superseding = result.Value!;
        db.CrmRequirementItems.Add(superseding);
        await db.SaveChangesAsync(ct);

        return superseding.Id;
    }
}
