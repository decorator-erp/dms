using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Spaces;

internal sealed class CreateCrmSpaceCommandValidator : AbstractValidator<CreateCrmSpaceCommand>
{
    public CreateCrmSpaceCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.SpaceTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();
    }
}

/// <summary><see cref="CreateCrmSpaceCommand.ProjectId"/> is validated against a real project via
/// <see cref="IProjectDirectory"/> — carries no FK (structural item 1).</summary>
internal sealed class CreateCrmSpaceCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateCrmSpaceCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmSpaceCommand command, CancellationToken ct = default)
    {
        ProjectSummary? project = await projectDirectory.GetProjectAsync(command.ProjectId, ct);
        if (project is null)
        {
            return Result.Failure<Guid>(CrmSpaceErrors.SpaceNotFound);
        }

        Result<CrmSpace> result = CrmSpace.Create(command.ProjectId, command.SpaceTypeId, command.Name, command.SortOrder, command.Description);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmSpace space = result.Value!;
        db.CrmSpaces.Add(space);
        await db.SaveChangesAsync(ct);

        return space.Id;
    }
}

internal sealed class UpdateCrmSpaceCommandValidator : AbstractValidator<UpdateCrmSpaceCommand>
{
    public UpdateCrmSpaceCommandValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.SpaceTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();
    }
}

internal sealed class UpdateCrmSpaceCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmSpaceCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmSpaceCommand command, CancellationToken ct = default)
    {
        CrmSpace? space = await db.CrmSpaces.FindAsync([command.SpaceId], ct);
        if (space is null)
        {
            return CrmSpaceErrors.SpaceNotFound;
        }

        Result result = space.Update(command.SpaceTypeId, command.Name, command.Description);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class SetCrmSpaceActiveCommandValidator : AbstractValidator<SetCrmSpaceActiveCommand>
{
    public SetCrmSpaceActiveCommandValidator() => RuleFor(x => x.SpaceId).NotEmpty();
}

internal sealed class SetCrmSpaceActiveCommandHandler(CrmDbContext db) : ICommandHandler<SetCrmSpaceActiveCommand>
{
    public async Task<Result> HandleAsync(SetCrmSpaceActiveCommand command, CancellationToken ct = default)
    {
        CrmSpace? space = await db.CrmSpaces.FindAsync([command.SpaceId], ct);
        if (space is null)
        {
            return CrmSpaceErrors.SpaceNotFound;
        }

        if (command.IsActive)
        {
            space.Activate();
        }
        else
        {
            space.Deactivate();
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
