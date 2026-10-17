using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Documents.Application.Features.FileTemplates.Delete;

internal sealed class DeleteFileTemplateCommandValidator : AbstractValidator<DeleteFileTemplateCommand>
{
    public DeleteFileTemplateCommandValidator() => RuleFor(x => x.FileTemplateId).NotEmpty();
}

/// <summary>
/// Soft-deletes the row (<c>IsActive = false</c>) but, unlike <c>DeleteProjectDocumentCommand</c>, also
/// deletes the underlying blob — a deliberate difference from G7-02's "keep the blob" correction, not an
/// inconsistency. <c>ProjectDocument</c>'s <c>IsActive</c> flip is the ONLY reversible-hide mechanism it
/// has, so destroying its bytes would make undelete a lie. <c>FileTemplate</c> already has a separate,
/// dedicated reversible-hide pair (<see cref="ActivateFileTemplateCommand"/>/
/// <see cref="DeactivateFileTemplateCommand"/>) distinct from this command — <c>Delete</c> here means
/// genuinely gone, matching the backend's own behavior for this entity.
/// </summary>
internal sealed class DeleteFileTemplateCommandHandler(DocumentsDbContext db, IFileStorageService fileStorage)
    : ICommandHandler<DeleteFileTemplateCommand>
{
    public async Task<Result> HandleAsync(DeleteFileTemplateCommand command, CancellationToken ct = default)
    {
        FileTemplate? template = await db.FileTemplates.FindAsync([command.FileTemplateId], ct);
        if (template is null)
        {
            return FileTemplateErrors.NotFound;
        }

        template.Deactivate();
        await db.SaveChangesAsync(ct);

        await fileStorage.DeleteAsync(template.StoragePath, ct);

        return Result.Success();
    }
}
