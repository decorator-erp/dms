using BuildingBlocks.Messaging;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
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

internal sealed class SupersedeCrmRequirementItemCommandHandler(CrmDbContext db)
    : ICommandHandler<SupersedeCrmRequirementItemCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SupersedeCrmRequirementItemCommand command, CancellationToken ct = default)
    {
        var original = await db.CrmRequirementItems.FindAsync([command.OriginalItemId], ct);
        if (original is null)
            return Result.Failure<Guid>(CrmItemErrors.NotFound);

        var result = CrmRequirementItem.CreateSuperseding(
            original, command.EmployeeId, CrmEnumMapping.ToDomain(command.Category), command.Title,
            CrmEnumMapping.ToDomain(command.Priority), command.SpaceTypeId, command.Room, command.Description,
            command.SourceType is { } st ? CrmEnumMapping.ToDomain(st) : null,
            command.MeetingId, command.BriefId, command.NoteId, command.ProjectTaskId);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        var superseding = result.Value!;
        db.CrmRequirementItems.Add(superseding);
        await db.SaveChangesAsync(ct);

        return superseding.Id;
    }
}
