using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Items;

internal sealed class CreateCrmRequirementItemByClientCommandValidator : AbstractValidator<CreateCrmRequirementItemByClientCommand>
{
    public CreateCrmRequirementItemByClientCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        // X-1: bounded like the columns (CrmRequirementItemConfiguration), so an over-long value is a 400
        // rather than a database error.
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Room).MaximumLength(128);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
    }
}

/// <summary>The only Portal write in this sub-area — trusted immediately, no Send step. See
/// <c>CrmRequirementItem.CreateByClient</c>'s own remarks.
///
/// <para>X-1: the item must land in an open period of the contact's own client's project. A project that
/// is not theirs, or does not exist, and a period of another project are the portal's 404, so a caller
/// cannot probe what exists. A closed period is 409; an unknown or inactive space type is 400.</para></summary>
internal sealed class CreateCrmRequirementItemByClientCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmRequirementItemByClientCommand command, CancellationToken ct = default)
    {
        Guid? clientId = await db.CrmContacts.AsNoTracking()
            .Where(c => c.Id == command.ContactId)
            .Select(c => (Guid?)c.ClientId)
            .FirstOrDefaultAsync(ct);
        ProjectSummary? project = clientId is null ? null : await projectDirectory.GetProjectAsync(command.ProjectId, ct);
        if (project is null || project.ClientId != clientId)
        {
            return Result.Failure<Guid>(CrmPortalErrors.NotFound);
        }

        var period = await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => p.Id == command.PeriodId && p.ProjectId == command.ProjectId)
            .Select(p => new { p.Status })
            .FirstOrDefaultAsync(ct);
        if (period is null)
        {
            return Result.Failure<Guid>(CrmPortalErrors.NotFound);
        }

        if (period.Status != RequirementPeriodStatus.Open)
        {
            return Result.Failure<Guid>(CrmPeriodErrors.NotOpen);
        }

        Result spaceType = await ItemReferenceValidator.ValidateSpaceTypeAsync(db, command.SpaceTypeId, ct);
        if (spaceType.IsFailure)
        {
            return Result.Failure<Guid>(spaceType.Error);
        }

        var item = CrmRequirementItem.CreateByClient(
            command.ProjectId, command.PeriodId, command.ContactId, CrmEnumMapping.ToDomain(command.Category),
            command.Title, CrmEnumMapping.ToDomain(command.Priority), command.SpaceTypeId, command.Room,
            command.Description);

        db.CrmRequirementItems.Add(item);
        Result saved = await ItemWrites.SaveAsync(db, ct);

        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : item.Id;
    }
}
