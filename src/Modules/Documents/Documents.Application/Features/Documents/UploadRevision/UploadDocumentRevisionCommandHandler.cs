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

namespace Documents.Application.Features.Documents.UploadRevision;

internal sealed class UploadDocumentRevisionCommandValidator : AbstractValidator<UploadDocumentRevisionCommand>
{
    public UploadDocumentRevisionCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.FileSizeBytes).GreaterThan(0);
        RuleFor(x => x.UploadedByUserId).NotEmpty();
    }
}

/// <summary><paramref name="DocumentId"/> may be ANY revision in a lineage - the handler always
/// resolves the true latest version by <c>RootDocumentId</c> before creating a new one, matching the
/// backend's own "revision always applies to the current tip" semantics.</summary>
internal sealed class UploadDocumentRevisionCommandHandler(
    DocumentsDbContext db, IFileStorageService fileStorage, IHrDirectory hrDirectory, IProjectDirectory projectDirectory)
    : ICommandHandler<UploadDocumentRevisionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(UploadDocumentRevisionCommand command, CancellationToken ct = default)
    {
        ProjectDocument? referenced = await db.ProjectDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId, ct);
        if (referenced is null)
        {
            return ProjectDocumentErrors.NotFound;
        }

        ProjectDocument latest = await db.ProjectDocuments
            .Where(d => d.RootDocumentId == referenced.RootDocumentId)
            .OrderByDescending(d => d.Version)
            .FirstAsync(ct);

        Guid? uploadedById = await hrDirectory.ResolveEmployeeIdAsync(command.UploadedByUserId, ct);
        if (uploadedById is null)
        {
            return ProjectDocumentErrors.UploaderNotFound;
        }

        string storagePath = await fileStorage.SaveAsync(
            command.Content, command.FileName, $"projects/{latest.ProjectId}", ct);

        Result<ProjectDocument> revisionResult = ProjectDocument.CreateRevision(
            latest, command.FileName, storagePath, command.ContentType, command.FileSizeBytes,
            uploadedById.Value, command.Description);
        if (revisionResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            return Result.Failure<Guid>(revisionResult.Error);
        }

        ProjectDocument revision = revisionResult.Value!;
        string? displayName = revision.ProjectTaskId is { } taskId ? (await projectDirectory.GetTaskAsync(taskId, ct))?.Name
            : revision.ProjectStageId is { } stageId ? (await projectDirectory.GetStageAsync(stageId, ct))?.Name
            : (await projectDirectory.GetProjectAsync(revision.ProjectId, ct))?.Name;
        await DocumentUploadCommit.CommitAsync(
            db, fileStorage, storagePath, revision, ct, UploadActivity.For(command.UploadedByUserId, revision, displayName));

        return revision.Id;
    }
}
