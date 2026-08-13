using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Folders.CreateForTask;

internal sealed class CreateTaskFolderCommandValidator : AbstractValidator<CreateTaskFolderCommand>
{
    public CreateTaskFolderCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ProjectTaskId).NotEmpty();
    }
}

/// <summary>Mirrors <c>CreateStageFolderCommandHandler</c> exactly, one scope down.</summary>
internal sealed class CreateTaskFolderCommandHandler(DocumentsDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateTaskFolderCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateTaskFolderCommand command, CancellationToken ct = default)
    {
        var task = await projectDirectory.GetTaskAsync(command.ProjectTaskId, ct);
        if (task is null)
            return DocumentFolderErrors.NotFound;

        if (command.ParentFolderId is { } parentFolderId)
        {
            var parentExists = await db.DocumentFolders.AsNoTracking()
                .AnyAsync(f => f.Id == parentFolderId && f.ProjectId == task.ProjectId, ct);
            if (!parentExists)
                return DocumentFolderErrors.NotFound;
        }

        var nameTaken = await db.DocumentFolders.AsNoTracking().AnyAsync(
            f => f.ProjectId == task.ProjectId && f.ParentFolderId == command.ParentFolderId
                && f.Name == command.Name, ct);
        if (nameTaken)
            return DocumentFolderErrors.NameCollision;

        var folderResult = DocumentFolder.Create(
            task.ProjectId, command.Name, command.ParentFolderId, projectTaskId: command.ProjectTaskId);
        if (folderResult.IsFailure)
            return Result.Failure<Guid>(folderResult.Error);

        var folder = folderResult.Value!;
        db.DocumentFolders.Add(folder);
        await db.SaveChangesAsync(ct);

        return folder.Id;
    }
}
