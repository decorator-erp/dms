using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Folders.CreateForStage;

internal sealed class CreateStageFolderCommandValidator : AbstractValidator<CreateStageFolderCommand>
{
    public CreateStageFolderCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ProjectStageId).NotEmpty();
    }
}

/// <summary>Resolves the owning project via <see cref="IProjectDirectory"/> — a stage-scoped folder
/// carries no direct project id of its own on the command, unlike <c>CreateDocumentFolderCommand</c>,
/// and Documents has no other way to see whether the stage even exists.</summary>
internal sealed class CreateStageFolderCommandHandler(DocumentsDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateStageFolderCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageFolderCommand command, CancellationToken ct = default)
    {
        StageSummary? stage = await projectDirectory.GetStageAsync(command.ProjectStageId, ct);
        if (stage is null)
        {
            return DocumentFolderErrors.NotFound;
        }

        if (command.ParentFolderId is { } parentFolderId)
        {
            bool parentExists = await db.DocumentFolders.AsNoTracking()
                .AnyAsync(f => f.Id == parentFolderId && f.ProjectId == stage.ProjectId, ct);
            if (!parentExists)
            {
                return DocumentFolderErrors.NotFound;
            }
        }

        bool nameTaken = await db.DocumentFolders.AsNoTracking().AnyAsync(
            f => f.ProjectId == stage.ProjectId && f.ParentFolderId == command.ParentFolderId
                && f.Name == command.Name, ct);
        if (nameTaken)
        {
            return DocumentFolderErrors.NameCollision;
        }

        Result<DocumentFolder> folderResult = DocumentFolder.Create(
            stage.ProjectId, command.Name, command.ParentFolderId, projectStageId: command.ProjectStageId);
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
