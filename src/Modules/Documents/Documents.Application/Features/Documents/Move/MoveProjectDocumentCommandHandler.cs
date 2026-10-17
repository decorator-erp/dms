using BuildingBlocks.Messaging;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.Move;

internal sealed class MoveProjectDocumentCommandValidator : AbstractValidator<MoveProjectDocumentCommand>
{
    public MoveProjectDocumentCommandValidator() => RuleFor(x => x.DocumentId).NotEmpty();
}

internal sealed class MoveProjectDocumentCommandHandler(DocumentsDbContext db) : ICommandHandler<MoveProjectDocumentCommand>
{
    public async Task<Result> HandleAsync(MoveProjectDocumentCommand command, CancellationToken ct = default)
    {
        ProjectDocument? document = await db.ProjectDocuments.FindAsync([command.DocumentId], ct);
        if (document is null)
        {
            return ProjectDocumentErrors.NotFound;
        }

        if (command.FolderId is { } folderId)
        {
            bool folderExists = await db.DocumentFolders.AsNoTracking()
                .AnyAsync(f => f.Id == folderId && f.ProjectId == document.ProjectId, ct);
            if (!folderExists)
            {
                return ProjectDocumentErrors.FolderScopeMismatch;
            }
        }

        Result moveResult = document.MoveToFolder(command.FolderId);
        if (moveResult.IsFailure)
        {
            return moveResult;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
