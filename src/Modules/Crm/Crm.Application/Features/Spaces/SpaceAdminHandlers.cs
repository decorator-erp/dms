using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel;

namespace Crm.Application.Features.Spaces;

internal sealed class ReorderCrmSpacesCommandValidator : AbstractValidator<ReorderCrmSpacesCommand>
{
    public ReorderCrmSpacesCommandValidator() => RuleFor(x => x.SpaceIds).NotEmpty().Must(ids => ids.Count <= 500);
}

/// <summary>crm-complete F5. Behind the CRM project scope (404). The list must be exactly the project's spaces,
/// each once; positions become SortOrder 1..n.</summary>
internal sealed class ReorderCrmSpacesCommandHandler(CrmDbContext db) : ICommandHandler<ReorderCrmSpacesCommand>
{
    public async Task<Result> HandleAsync(ReorderCrmSpacesCommand command, CancellationToken ct = default)
    {
        List<CrmSpace> spaces = await db.CrmSpaces.Where(s => s.ProjectId == command.ProjectId).ToListAsync(ct);
        if (command.SpaceIds.Distinct().Count() != command.SpaceIds.Count
            || spaces.Count != command.SpaceIds.Count
            || !spaces.Select(s => s.Id).ToHashSet().SetEquals(command.SpaceIds))
        {
            return CrmSpaceErrors.InvalidOrder;
        }

        var byId = spaces.ToDictionary(s => s.Id);
        for (int i = 0; i < command.SpaceIds.Count; i++)
        {
            byId[command.SpaceIds[i]].SetSortOrder(i + 1);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>F5 (D4): hard delete only when no brief-space entry, space item, artifact link or storefront selection
/// references the space, decided in the delete's own transaction with the space row locked (<c>FOR UPDATE</c>);
/// an FK violation on the save is still 409.</summary>
internal sealed class DeleteCrmSpaceCommandHandler(CrmDbContext db) : ICommandHandler<DeleteCrmSpaceCommand>
{
    public async Task<Result> HandleAsync(DeleteCrmSpaceCommand command, CancellationToken ct = default)
    {
        IDbContextTransaction? own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            Result result = await DeleteAsync(command.SpaceId, ct);
            if (own is not null)
            {
                await (result.IsSuccess ? own.CommitAsync(ct) : own.RollbackAsync(ct));
            }

            return result;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    private async Task<Result> DeleteAsync(Guid spaceId, CancellationToken ct)
    {
        List<Guid> locked = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM crm.crm_spaces WHERE \"Id\" = {spaceId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
        {
            return CrmSpaceErrors.SpaceNotFound;
        }

        bool inUse = await db.CrmBriefSpaces.AnyAsync(e => e.SpaceId == spaceId, ct)
            || await db.CrmSpaceItems.AnyAsync(i => i.SpaceId == spaceId, ct)
            || await db.CrmArtifactDocuments.AnyAsync(a => a.SpaceId == spaceId, ct);
        if (inUse)
        {
            return CrmSpaceErrors.InUse;
        }

        db.CrmSpaces.Remove(await db.CrmSpaces.SingleAsync(s => s.Id == spaceId, ct));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return CrmSpaceErrors.InUse;
        }

        return Result.Success();
    }
}
