using BuildingBlocks.Messaging;
using Crm.Contracts.Artifacts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Documents.Contracts;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Artifacts;

internal sealed class LinkCrmArtifactDocumentCommandValidator : AbstractValidator<LinkCrmArtifactDocumentCommand>
{
    public LinkCrmArtifactDocumentCommandValidator() => RuleFor(x => x.DocumentId).NotEmpty();
}

/// <summary>
/// <see cref="LinkCrmArtifactDocumentCommand.DocumentId"/> carries no FK — structural item 1's
/// migration-order cycle (crm precedes projects, which precedes documents, so crm cannot reference
/// documents). Validated here against <see cref="IDocumentQueries.GetAsync"/> instead — the entire
/// replacement for the dropped constraint. An unknown id is rejected here, not by the database.
/// </summary>
internal sealed class LinkCrmArtifactDocumentCommandHandler(CrmDbContext db, IDocumentQueries documentQueries)
    : ICommandHandler<LinkCrmArtifactDocumentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(LinkCrmArtifactDocumentCommand command, CancellationToken ct = default)
    {
        var document = await documentQueries.GetAsync(command.DocumentId, ct);
        if (document is null)
            return Result.Failure<Guid>(CrmArtifactErrors.DocumentNotFound);

        var result = CrmArtifactDocument.Create(
            command.DocumentId, command.BriefId, command.SpaceId, command.RequirementItemId, command.MeetingId,
            command.AddedByEmployeeId, command.AddedByContactId);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        var artifact = result.Value!;
        db.CrmArtifactDocuments.Add(artifact);
        await db.SaveChangesAsync(ct);

        return artifact.Id;
    }
}
