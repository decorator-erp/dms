using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Hr.Contracts;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Documents.ApplyTemplate;

internal sealed class ApplyFileTemplateToTaskCommandValidator : AbstractValidator<ApplyFileTemplateToTaskCommand>
{
    public ApplyFileTemplateToTaskCommandValidator()
    {
        RuleFor(x => x.ProjectTaskId).NotEmpty();
        RuleFor(x => x.FileTemplateId).NotEmpty();
        RuleFor(x => x.UploadedByUserId).NotEmpty();
    }
}

/// <summary>Copies the template's stored bytes into a fresh blob (never shares the template's own
/// <c>StoragePath</c> with the new document — the two must be independently deletable) and always
/// resolves/creates the task's default folder, unlike the plain task upload which only does so when no
/// explicit folder is given.</summary>
internal sealed class ApplyFileTemplateToTaskCommandHandler(
    DocumentsDbContext db, IFileStorageService fileStorage, IHrDirectory hrDirectory, IProjectDirectory projectDirectory)
    : ICommandHandler<ApplyFileTemplateToTaskCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ApplyFileTemplateToTaskCommand command, CancellationToken ct = default)
    {
        var task = await projectDirectory.GetTaskAsync(command.ProjectTaskId, ct);
        if (task is null)
            return ProjectDocumentErrors.NotFound;

        var template = await db.FileTemplates.FindAsync([command.FileTemplateId], ct);
        if (template is null || !template.IsActive)
            return FileTemplateErrors.NotFound;

        var uploadedById = await hrDirectory.ResolveEmployeeIdAsync(command.UploadedByUserId, ct);
        if (uploadedById is null)
            return ProjectDocumentErrors.UploaderNotFound;

        await using var templateContent = await fileStorage.OpenReadAsync(template.StoragePath, ct);
        if (templateContent is null)
            return FileTemplateErrors.NotFound;

        var folderId = await DefaultFolderResolver.EnsureForTaskAsync(
            db, task.ProjectId, command.ProjectTaskId, task.Name, ct);

        var storagePath = await fileStorage.SaveAsync(
            templateContent, template.FileName, $"projects/{task.ProjectId}", ct);

        var documentResult = ProjectDocument.Create(
            task.ProjectId, template.FileName, storagePath, template.ContentType, template.FileSizeBytes,
            uploadedById.Value, folderId, projectTaskId: command.ProjectTaskId);
        if (documentResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            return Result.Failure<Guid>(documentResult.Error);
        }

        var document = documentResult.Value!;
        await DocumentUploadCommit.CommitAsync(db, fileStorage, storagePath, document, ct);

        return document.Id;
    }
}
