using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class CreateCrmRequirementItemCommandValidator : AbstractValidator<CreateCrmRequirementItemCommand>
{
    public CreateCrmRequirementItemCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty();
    }
}

internal sealed class CreateCrmRequirementItemCommandHandler(CrmDbContext db)
    : ICommandHandler<CreateCrmRequirementItemCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        bool periodExists = await db.CrmRequirementPeriods.AnyAsync(p => p.Id == command.PeriodId, ct);
        if (!periodExists)
        {
            return Result.Failure<Guid>(CrmPeriodErrors.NotFound);
        }

        Result<CrmRequirementItem> result = CrmRequirementItem.CreateByEmployee(
            command.ProjectId, command.PeriodId, command.EmployeeId, CrmEnumMapping.ToDomain(command.Category),
            command.Title, CrmEnumMapping.ToDomain(command.Priority), command.SpaceTypeId, command.Room,
            command.Description, command.SourceType is { } st ? CrmEnumMapping.ToDomain(st) : null,
            command.MeetingId, command.BriefId, command.NoteId, command.ProjectTaskId);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmRequirementItem item = result.Value!;
        db.CrmRequirementItems.Add(item);
        await db.SaveChangesAsync(ct);

        return item.Id;
    }
}
