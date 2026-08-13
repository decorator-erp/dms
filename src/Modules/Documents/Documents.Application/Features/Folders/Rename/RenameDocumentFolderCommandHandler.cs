using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Folders.Rename;

internal sealed class RenameDocumentFolderCommandValidator : AbstractValidator<RenameDocumentFolderCommand>
{
    public RenameDocumentFolderCommandValidator()
    {
        RuleFor(x => x.FolderId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}

internal sealed class RenameDocumentFolderCommandHandler(DocumentsDbContext db) : ICommandHandler<RenameDocumentFolderCommand>
{
    public async Task<Result> HandleAsync(RenameDocumentFolderCommand command, CancellationToken ct = default)
    {
        var folder = await db.DocumentFolders.FindAsync([command.FolderId], ct);
        if (folder is null)
            return DocumentFolderErrors.NotFound;

        var nameTaken = await db.DocumentFolders.AsNoTracking().AnyAsync(
            f => f.Id != folder.Id && f.ProjectId == folder.ProjectId
                && f.ParentFolderId == folder.ParentFolderId && f.Name == command.Name, ct);
        if (nameTaken)
            return DocumentFolderErrors.NameCollision;

        var renameResult = folder.Rename(command.Name);
        if (renameResult.IsFailure)
            return renameResult;

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
