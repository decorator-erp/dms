using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Documents.UploadToTask;

internal sealed class UploadTaskDocumentCommandValidator : AbstractValidator<UploadTaskDocumentCommand>
{
    public UploadTaskDocumentCommandValidator()
    {
        RuleFor(x => x.ProjectTaskId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.FileSizeBytes).GreaterThan(0);
        RuleFor(x => x.UploadedByUserId).NotEmpty();
    }
}

/// <summary>Mirrors <c>UploadStageDocumentCommandHandler</c> exactly, one scope down.</summary>
internal sealed class UploadTaskDocumentCommandHandler(
    DocumentsDbContext db, IFileStorageService fileStorage, IHrDirectory hrDirectory, IProjectDirectory projectDirectory)
    : ICommandHandler<UploadTaskDocumentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(UploadTaskDocumentCommand command, CancellationToken ct = default)
    {
        TaskSummary? task = await projectDirectory.GetTaskAsync(command.ProjectTaskId, ct);
        if (task is null)
        {
            return ProjectDocumentErrors.NotFound;
        }

        Guid? uploadedById = await hrDirectory.ResolveEmployeeIdAsync(command.UploadedByUserId, ct);
        if (uploadedById is null)
        {
            return ProjectDocumentErrors.UploaderNotFound;
        }

        Guid folderId;
        if (command.FolderId is { } explicitFolderId)
        {
            bool folderMatchesScope = await db.DocumentFolders.AsNoTracking()
                .AnyAsync(f => f.Id == explicitFolderId && f.ProjectTaskId == command.ProjectTaskId, ct);
            if (!folderMatchesScope)
            {
                return ProjectDocumentErrors.FolderScopeMismatch;
            }

            folderId = explicitFolderId;
        }
        else
        {
            folderId = await DefaultFolderResolver.EnsureForTaskAsync(
                db, task.ProjectId, command.ProjectTaskId, task.Name, ct);
        }

        string storagePath = await fileStorage.SaveAsync(
            command.Content, command.FileName, $"projects/{task.ProjectId}", ct);

        Result<ProjectDocument> documentResult = ProjectDocument.Create(
            task.ProjectId, command.FileName, storagePath, command.ContentType, command.FileSizeBytes,
            uploadedById.Value, folderId, projectStageId: task.ProjectStageId,
            projectTaskId: command.ProjectTaskId, description: command.Description);
        if (documentResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            return Result.Failure<Guid>(documentResult.Error);
        }

        ProjectDocument document = documentResult.Value!;
        await DocumentUploadCommit.CommitAsync(
            db, fileStorage, storagePath, document, ct, UploadActivity.For(command.UploadedByUserId, document, task.Name));

        return document.Id;
    }
}
