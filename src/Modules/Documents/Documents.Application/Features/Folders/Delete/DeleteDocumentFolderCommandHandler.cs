using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Documents.Application.Features.Folders.Delete;

internal sealed class DeleteDocumentFolderCommandValidator : AbstractValidator<DeleteDocumentFolderCommand>
{
    public DeleteDocumentFolderCommandValidator() => RuleFor(x => x.FolderId).NotEmpty();
}

/// <summary>Soft delete only — <c>IsActive = false</c>, matching the backend exactly. Sub-folders and
/// documents are left exactly where they are; there is no cascade, and this handler doesn't check for
/// or block on children existing (the backend doesn't either) — the DB's own <c>Restrict</c>/<c>SetNull</c>
/// FKs (G7-03) are what govern an actual hard-delete path, which this never attempts.</summary>
internal sealed class DeleteDocumentFolderCommandHandler(DocumentsDbContext db) : ICommandHandler<DeleteDocumentFolderCommand>
{
    public async Task<Result> HandleAsync(DeleteDocumentFolderCommand command, CancellationToken ct = default)
    {
        DocumentFolder? folder = await db.DocumentFolders.FindAsync([command.FolderId], ct);
        if (folder is null)
        {
            return DocumentFolderErrors.NotFound;
        }

        folder.Deactivate();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
