using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class CreateCrmRequirementItemByClientCommandValidator : AbstractValidator<CreateCrmRequirementItemByClientCommand>
{
    public CreateCrmRequirementItemByClientCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty();
    }
}

/// <summary>The only Portal write in this sub-area — trusted immediately, no Send step. See
/// <c>CrmRequirementItem.CreateByClient</c>'s own remarks.</summary>
internal sealed class CreateCrmRequirementItemByClientCommandHandler(CrmDbContext db)
    : ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmRequirementItemByClientCommand command, CancellationToken ct = default)
    {
        var periodExists = await db.CrmRequirementPeriods.AnyAsync(p => p.Id == command.PeriodId, ct);
        if (!periodExists)
            return Result.Failure<Guid>(CrmPeriodErrors.NotFound);

        var item = CrmRequirementItem.CreateByClient(
            command.ProjectId, command.PeriodId, command.ContactId, CrmEnumMapping.ToDomain(command.Category),
            command.Title, CrmEnumMapping.ToDomain(command.Priority), command.SpaceTypeId, command.Room,
            command.Description);

        db.CrmRequirementItems.Add(item);
        await db.SaveChangesAsync(ct);

        return item.Id;
    }
}
