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

namespace Documents.Application.Features.Documents.UploadToStage;

internal sealed class UploadStageDocumentCommandValidator : AbstractValidator<UploadStageDocumentCommand>
{
    public UploadStageDocumentCommandValidator()
    {
        RuleFor(x => x.ProjectStageId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.FileSizeBytes).GreaterThan(0);
        RuleFor(x => x.UploadedByUserId).NotEmpty();
    }
}

/// <summary>No explicit <c>FolderId</c> resolves/creates the stage's default folder
/// (<c>DefaultFolderResolver</c>, G7-06). An explicit one must already be scoped to this stage.</summary>
internal sealed class UploadStageDocumentCommandHandler(
    DocumentsDbContext db, IFileStorageService fileStorage, IHrDirectory hrDirectory, IProjectDirectory projectDirectory)
    : ICommandHandler<UploadStageDocumentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(UploadStageDocumentCommand command, CancellationToken ct = default)
    {
        StageSummary? stage = await projectDirectory.GetStageAsync(command.ProjectStageId, ct);
        if (stage is null)
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
                .AnyAsync(f => f.Id == explicitFolderId && f.ProjectStageId == command.ProjectStageId, ct);
            if (!folderMatchesScope)
            {
                return ProjectDocumentErrors.FolderScopeMismatch;
            }

            folderId = explicitFolderId;
        }
        else
        {
            folderId = await DefaultFolderResolver.EnsureForStageAsync(
                db, stage.ProjectId, command.ProjectStageId, stage.Name, ct);
        }

        string storagePath = await fileStorage.SaveAsync(
            command.Content, command.FileName, $"projects/{stage.ProjectId}", ct);

        Result<ProjectDocument> documentResult = ProjectDocument.Create(
            stage.ProjectId, command.FileName, storagePath, command.ContentType, command.FileSizeBytes,
            uploadedById.Value, folderId, command.ProjectStageId, description: command.Description);
        if (documentResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            return Result.Failure<Guid>(documentResult.Error);
        }

        ProjectDocument document = documentResult.Value!;
        await DocumentUploadCommit.CommitAsync(
            db, fileStorage, storagePath, document, ct, UploadActivity.For(command.UploadedByUserId, document, stage.Name));

        return document.Id;
    }
}
