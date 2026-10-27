using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class CreateCrmRequirementItemCommandValidator : AbstractValidator<CreateCrmRequirementItemCommand>
{
    public CreateCrmRequirementItemCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.EmployeeId).NotEmpty();
        // A-8 (B-M5): bounded like the columns (CrmRequirementItemConfiguration).
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Room).MaximumLength(128);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.SourceType).IsInEnum();
    }
}

/// <summary>A-3 (X-6): the item's project is the period's — the body's <c>ProjectId</c> must agree —
/// and every reference it carries is checked against that project.</summary>
internal sealed class CreateCrmRequirementItemCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateCrmRequirementItemCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        Guid? periodProjectId = await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => p.Id == command.PeriodId)
            .Select(p => (Guid?)p.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (periodProjectId is not { } projectId)
        {
            return Result.Failure<Guid>(CrmPeriodErrors.NotFound);
        }

        if (command.ProjectId != projectId)
        {
            return Result.Failure<Guid>(CrmItemErrors.ProjectMismatch);
        }

        Result open = await ItemReferenceValidator.EnsurePeriodOpenAsync(db, command.PeriodId, ct);
        if (open.IsFailure)
        {
            return Result.Failure<Guid>(open.Error);
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            return Result.Failure<Guid>(CrmPeriodErrors.NotFound);
        }

        Result spaceType = await ItemReferenceValidator.ValidateSpaceTypeAsync(db, command.SpaceTypeId, ct);
        if (spaceType.IsFailure)
        {
            return Result.Failure<Guid>(spaceType.Error);
        }

        Result references = await ItemReferenceValidator.ValidateAsync(
            db, projectDirectory, project, command.ProjectTaskId, command.MeetingId, command.BriefId, command.NoteId, ct);
        if (references.IsFailure)
        {
            return Result.Failure<Guid>(references.Error);
        }

        Result<CrmRequirementItem> result = CrmRequirementItem.CreateByEmployee(
            projectId, command.PeriodId, command.EmployeeId, CrmEnumMapping.ToDomain(command.Category),
            command.Title, CrmEnumMapping.ToDomain(command.Priority), command.SpaceTypeId, command.Room,
            command.Description, command.SourceType is { } st ? CrmEnumMapping.ToDomain(st) : null,
            command.MeetingId, command.BriefId, command.NoteId, command.ProjectTaskId);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmRequirementItem item = result.Value!;
        db.CrmRequirementItems.Add(item);
        Result saved = await ItemWrites.SaveAsync(db, ct);

        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : item.Id;
    }
}
