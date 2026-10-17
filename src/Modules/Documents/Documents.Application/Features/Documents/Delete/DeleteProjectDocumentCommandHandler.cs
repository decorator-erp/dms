using BuildingBlocks.Messaging;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Documents.Application.Features.Documents.Delete;

internal sealed class DeleteProjectDocumentCommandValidator : AbstractValidator<DeleteProjectDocumentCommand>
{
    public DeleteProjectDocumentCommandValidator() => RuleFor(x => x.DocumentId).NotEmpty();
}

/// <summary>Soft delete only — the blob is deliberately KEPT (G7-02's correction 4), unlike the
/// backend's own hard-delete-the-blob behavior. Rejected while locked, same as move.</summary>
internal sealed class DeleteProjectDocumentCommandHandler(DocumentsDbContext db) : ICommandHandler<DeleteProjectDocumentCommand>
{
    public async Task<Result> HandleAsync(DeleteProjectDocumentCommand command, CancellationToken ct = default)
    {
        ProjectDocument? document = await db.ProjectDocuments.FindAsync([command.DocumentId], ct);
        if (document is null)
        {
            return ProjectDocumentErrors.NotFound;
        }

        Result deactivateResult = document.Deactivate();
        if (deactivateResult.IsFailure)
        {
            return deactivateResult;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
