using BuildingBlocks.Messaging;
using Crm.Contracts.Artifacts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Documents.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
        // FINDING-P1-14: the document must be the subject's own project's — another project's document is
        // "not found" from here, the same answer as an unknown id (no cross-project probing).
        DocumentSummary? document = await documentQueries.GetAsync(command.DocumentId, ct);
        if (document is null || document.ProjectId != await SubjectProjectIdAsync(command, ct))
        {
            return Result.Failure<Guid>(CrmArtifactErrors.DocumentNotFound);
        }

        Result<CrmArtifactDocument> result = CrmArtifactDocument.Create(
            command.DocumentId, command.BriefId, command.SpaceId, command.RequirementItemId, command.MeetingId,
            command.AddedByEmployeeId, command.AddedByContactId);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmArtifactDocument artifact = result.Value!;
        db.CrmArtifactDocuments.Add(artifact);
        // L-4: a second link of the same document to the same subject is 409, not a 500.
        Result saved = await ArtifactWrites.SaveAsync(db, ct);
        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : artifact.Id;
    }

    /// <summary>The project of the one subject the link names (null when none resolves; the domain then
    /// rejects the subject set).</summary>
    private async Task<Guid?> SubjectProjectIdAsync(LinkCrmArtifactDocumentCommand command, CancellationToken ct) =>
        command switch
        {
            { BriefId: { } id } => await db.CrmDesignBriefs.AsNoTracking().Where(b => b.Id == id).Select(b => (Guid?)b.ProjectId).FirstOrDefaultAsync(ct),
            { SpaceId: { } id } => await db.CrmSpaces.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct),
            { RequirementItemId: { } id } => await db.CrmRequirementItems.AsNoTracking().Where(i => i.Id == id).Select(i => (Guid?)i.ProjectId).FirstOrDefaultAsync(ct),
            { MeetingId: { } id } => await db.CrmMeetings.AsNoTracking().Where(m => m.Id == id).Select(m => (Guid?)m.ProjectId).FirstOrDefaultAsync(ct),
            _ => null,
        };
}
