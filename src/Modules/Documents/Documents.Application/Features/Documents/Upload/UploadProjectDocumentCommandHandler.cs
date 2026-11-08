using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Documents.Upload;

internal sealed class UploadProjectDocumentCommandValidator : AbstractValidator<UploadProjectDocumentCommand>
{
    public UploadProjectDocumentCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.FileSizeBytes).GreaterThan(0);
        RuleFor(x => x.UploadedByUserId).NotEmpty();
    }
}

/// <summary>Project-wide upload: no stage/task scope. An explicit <c>FolderId</c> must belong to the
/// same project.</summary>
internal sealed class UploadProjectDocumentCommandHandler(
    DocumentsDbContext db, IFileStorageService fileStorage, IHrDirectory hrDirectory, IProjectDirectory projectDirectory)
    : ICommandHandler<UploadProjectDocumentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(UploadProjectDocumentCommand command, CancellationToken ct = default)
    {
        Guid? uploadedById = await hrDirectory.ResolveEmployeeIdAsync(command.UploadedByUserId, ct);
        if (uploadedById is null)
        {
            return ProjectDocumentErrors.UploaderNotFound;
        }

        if (command.FolderId is { } folderId)
        {
            bool folderExists = await db.DocumentFolders.AsNoTracking()
                .AnyAsync(f => f.Id == folderId && f.ProjectId == command.ProjectId, ct);
            if (!folderExists)
            {
                return ProjectDocumentErrors.FolderScopeMismatch;
            }
        }

        string storagePath = await fileStorage.SaveAsync(
            command.Content, command.FileName, $"projects/{command.ProjectId}", ct);

        Result<ProjectDocument> documentResult = ProjectDocument.Create(
            command.ProjectId, command.FileName, storagePath, command.ContentType, command.FileSizeBytes,
            uploadedById.Value, command.FolderId, description: command.Description);
        if (documentResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            return Result.Failure<Guid>(documentResult.Error);
        }

        ProjectDocument document = documentResult.Value!;
        ProjectSummary? project = await projectDirectory.GetProjectAsync(document.ProjectId, ct);
        await DocumentUploadCommit.CommitAsync(
            db, fileStorage, storagePath, document, ct, UploadActivity.For(command.UploadedByUserId, document, project?.Name));

        return document.Id;
    }
}
