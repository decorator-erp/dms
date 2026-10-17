using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Folders.Create;

internal sealed class CreateDocumentFolderCommandValidator : AbstractValidator<CreateDocumentFolderCommand>
{
    public CreateDocumentFolderCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ProjectId).NotEmpty();
    }
}

internal sealed class CreateDocumentFolderCommandHandler(DocumentsDbContext db)
    : ICommandHandler<CreateDocumentFolderCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateDocumentFolderCommand command, CancellationToken ct = default)
    {
        if (command.ParentFolderId is { } parentFolderId)
        {
            bool parentExists = await db.DocumentFolders.AsNoTracking()
                .AnyAsync(f => f.Id == parentFolderId && f.ProjectId == command.ProjectId, ct);
            if (!parentExists)
            {
                return DocumentFolderErrors.NotFound;
            }
        }

        bool nameTaken = await db.DocumentFolders.AsNoTracking().AnyAsync(
            f => f.ProjectId == command.ProjectId && f.ParentFolderId == command.ParentFolderId
                && f.Name == command.Name, ct);
        if (nameTaken)
        {
            return DocumentFolderErrors.NameCollision;
        }

        Result<DocumentFolder> folderResult = DocumentFolder.Create(
            command.ProjectId, command.Name, command.ParentFolderId,
            command.ProjectStageId, command.ProjectTaskId);
        if (folderResult.IsFailure)
        {
            return Result.Failure<Guid>(folderResult.Error);
        }

        DocumentFolder folder = folderResult.Value!;
        db.DocumentFolders.Add(folder);
        await db.SaveChangesAsync(ct);

        return folder.Id;
    }
}
