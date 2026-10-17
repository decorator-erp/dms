using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Folders.Move;

internal sealed class MoveDocumentFolderCommandValidator : AbstractValidator<MoveDocumentFolderCommand>
{
    public MoveDocumentFolderCommandValidator() => RuleFor(x => x.FolderId).NotEmpty();
}

/// <summary>
/// New work versus the backend, per G7-02's correction 5 — the backend has no cycle guard at all here.
/// Loads the WHOLE project's folder-id → parent-id map once and hands it to
/// <see cref="DocumentFolder.MoveTo"/>, which is the pure cycle-detection algorithm (unit-tested at
/// G7-02 without a database). Also re-checks the sibling-name uniqueness the backend's own
/// <c>MoveDocumentFolderCommandHandler</c> skips (it throws a raw, unhandled <c>DbUpdateException</c> on
/// a name collision instead) — this validates before touching the database.
/// </summary>
internal sealed class MoveDocumentFolderCommandHandler(DocumentsDbContext db) : ICommandHandler<MoveDocumentFolderCommand>
{
    public async Task<Result> HandleAsync(MoveDocumentFolderCommand command, CancellationToken ct = default)
    {
        DocumentFolder? folder = await db.DocumentFolders.FindAsync([command.FolderId], ct);
        if (folder is null)
        {
            return DocumentFolderErrors.NotFound;
        }

        if (command.NewParentFolderId is { } newParentFolderId)
        {
            bool newParentExists = await db.DocumentFolders.AsNoTracking()
                .AnyAsync(f => f.Id == newParentFolderId && f.ProjectId == folder.ProjectId, ct);
            if (!newParentExists)
            {
                return DocumentFolderErrors.NotFound;
            }
        }

        bool nameTaken = await db.DocumentFolders.AsNoTracking().AnyAsync(
            f => f.Id != folder.Id && f.ProjectId == folder.ProjectId
                && f.ParentFolderId == command.NewParentFolderId && f.Name == folder.Name, ct);
        if (nameTaken)
        {
            return DocumentFolderErrors.NameCollision;
        }

        Dictionary<Guid, Guid?> ancestorChain = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.ProjectId == folder.ProjectId && f.Id != folder.Id)
            .Select(f => new { f.Id, f.ParentFolderId })
            .ToDictionaryAsync(f => f.Id, f => f.ParentFolderId, ct);

        Result moveResult = folder.MoveTo(command.NewParentFolderId, ancestorChain);
        if (moveResult.IsFailure)
        {
            return moveResult;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
