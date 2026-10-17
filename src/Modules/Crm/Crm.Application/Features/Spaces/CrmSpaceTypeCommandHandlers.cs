using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Spaces;

internal sealed class CreateCrmSpaceTypeCommandValidator : AbstractValidator<CreateCrmSpaceTypeCommand>
{
    public CreateCrmSpaceTypeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty();
    }
}

internal sealed class CreateCrmSpaceTypeCommandHandler(CrmDbContext db) : ICommandHandler<CreateCrmSpaceTypeCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmSpaceTypeCommand command, CancellationToken ct = default)
    {
        Result<CrmSpaceType> result = CrmSpaceType.Create(command.Code, command.NameEn, command.NameAr, command.SortOrder);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmSpaceType type = result.Value!;
        db.CrmSpaceTypes.Add(type);
        await db.SaveChangesAsync(ct);

        return type.Id;
    }
}

internal sealed class UpdateCrmSpaceTypeCommandValidator : AbstractValidator<UpdateCrmSpaceTypeCommand>
{
    public UpdateCrmSpaceTypeCommandValidator() => RuleFor(x => x.SpaceTypeId).NotEmpty();
}

internal sealed class UpdateCrmSpaceTypeCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmSpaceTypeCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmSpaceTypeCommand command, CancellationToken ct = default)
    {
        CrmSpaceType? type = await db.CrmSpaceTypes.FindAsync([command.SpaceTypeId], ct);
        if (type is null)
        {
            return CrmSpaceTypeErrors.CodeRequired;
        }

        Result result = type.Update(command.NameEn, command.NameAr, command.SortOrder);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class SetCrmSpaceTypeActiveCommandValidator : AbstractValidator<SetCrmSpaceTypeActiveCommand>
{
    public SetCrmSpaceTypeActiveCommandValidator() => RuleFor(x => x.SpaceTypeId).NotEmpty();
}

internal sealed class SetCrmSpaceTypeActiveCommandHandler(CrmDbContext db) : ICommandHandler<SetCrmSpaceTypeActiveCommand>
{
    public async Task<Result> HandleAsync(SetCrmSpaceTypeActiveCommand command, CancellationToken ct = default)
    {
        CrmSpaceType? type = await db.CrmSpaceTypes.FindAsync([command.SpaceTypeId], ct);
        if (type is null)
        {
            return CrmSpaceTypeErrors.CodeRequired;
        }

        if (command.IsActive)
        {
            type.Activate();
        }
        else
        {
            type.Deactivate();
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
