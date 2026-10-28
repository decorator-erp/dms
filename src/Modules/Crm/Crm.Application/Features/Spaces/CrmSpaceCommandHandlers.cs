using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Spaces;

internal sealed class CreateCrmSpaceCommandValidator : AbstractValidator<CreateCrmSpaceCommand>
{
    public CreateCrmSpaceCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.SpaceTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(1000);
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

        if (!await db.CrmSpaceTypes.AsNoTracking().AnyAsync(t => t.Id == command.SpaceTypeId, ct))
        {
            return Result.Failure<Guid>(CrmSpaceErrors.UnknownSpaceType);
        }

        if (await SpaceNames.TakenAsync(db, command.ProjectId, command.Name, exceptSpaceId: null, ct))
        {
            return Result.Failure<Guid>(CrmSpaceErrors.DuplicateName);
        }

        Result<CrmSpace> result = CrmSpace.Create(command.ProjectId, command.SpaceTypeId, command.Name, command.SortOrder, command.Description);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmSpace space = result.Value!;
        db.CrmSpaces.Add(space);
        Result saved = await CatalogWrites.SaveAsync(db, ct);

        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : space.Id;
    }
}

internal sealed class UpdateCrmSpaceCommandValidator : AbstractValidator<UpdateCrmSpaceCommand>
{
    public UpdateCrmSpaceCommandValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.SpaceTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(1000);
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

        if (!await db.CrmSpaceTypes.AsNoTracking().AnyAsync(t => t.Id == command.SpaceTypeId, ct))
        {
            return CrmSpaceErrors.UnknownSpaceType;
        }

        if (await SpaceNames.TakenAsync(db, space.ProjectId, command.Name, exceptSpaceId: space.Id, ct))
        {
            return CrmSpaceErrors.DuplicateName;
        }

        Result result = space.Update(command.SpaceTypeId, command.Name, command.Description);
        if (result.IsFailure)
        {
            return result;
        }

        return await CatalogWrites.SaveAsync(db, ct);
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

/// <summary>A-7 (B-M4): a space name is taken when another space on the project has it, ignoring case — the
/// same comparison as the <c>lower("Name")</c> unique index. D-16 / A-F6: both sides are compared in SQL as
/// <c>lower(trim(...))</c> (the stored name is already trimmed), so the pre-check and the index can never
/// disagree on padding or on how a character lowercases.</summary>
internal static class SpaceNames
{
    public static Task<bool> TakenAsync(CrmDbContext db, Guid projectId, string name, Guid? exceptSpaceId, CancellationToken ct) =>
        db.CrmSpaces
            .FromSql($"""SELECT * FROM crm.crm_spaces WHERE "ProjectId" = {projectId} AND lower("Name") = lower(trim({name}))""")
            .AsNoTracking()
            .AnyAsync(s => s.Id != exceptSpaceId, ct);
}
