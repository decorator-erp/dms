using BuildingBlocks.Messaging;
using Documents.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Application.Features.Surveys;
using Sites.Contracts.Documents;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Documents;

internal sealed class LinkSiteDocumentCommandValidator : AbstractValidator<LinkSiteDocumentCommand>
{
    public LinkSiteDocumentCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.AddedById).NotEmpty();
        RuleFor(x => x.Request.DocumentId).NotEmpty();
    }
}

/// <summary>
/// Links an existing Documents row to this survey or one of its records. Stores no file — see
/// <c>SiteDocument</c>.
///
/// <para>Defect B5's fix, in two parts. The backend never checks the document at all: a caller with
/// <c>sites.update</c> on project A could link project B's document to their survey and then read the id
/// back through <c>GET /documents</c> — a cross-project reference leak through this module. And it skips
/// the editable guard every record write has, so attachments could be added to an APPROVED survey.</para>
/// </summary>
internal sealed class LinkSiteDocumentCommandHandler(SitesDbContext db, IDocumentQueries documents)
    : ICommandHandler<LinkSiteDocumentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        LinkSiteDocumentCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        if (!site.IsEditable)
        {
            return SiteErrors.SiteLocked;
        }

        // Belt and braces with fk_site_documents_document: the FK proves the row exists, this proves it
        // belongs to the same project as the survey.
        DocumentSummary? document = await documents.GetAsync(command.Request.DocumentId, ct);
        if (document is null || document.ProjectId != site.ProjectId)
        {
            return SiteErrors.DocumentNotFound;
        }

        Guid? siteId = command.Request.RecordId is null ? command.SiteId : null;

        if (command.Request.RecordId is { } recordId)
        {
            // Defect B15's fix: the backend answers InvalidDocumentSubject (400) for a record belonging
            // to a DIFFERENT site and RecordNotFound (404) for one that does not exist -- distinguishing
            // "exists but isn't yours" from "doesn't exist", the exact oracle the unlink handler's own
            // comment says it avoids. Both are RecordNotFound here.
            if (!await db.SiteRecords.AnyAsync(r => r.Id == recordId && r.SiteId == command.SiteId, ct))
            {
                return SiteErrors.RecordNotFound;
            }
        }

        Result<SiteDocument> link = SiteDocument.Create(
            command.Request.DocumentId, SiteEnumMapping.ToDomain(command.Request.Kind),
            command.AddedById, siteId, command.Request.RecordId);

        if (link.IsFailure)
        {
            return link.Error;
        }

        db.SiteDocuments.Add(link.Value!);

        Result saved = await SiteWrites.SaveAsync(db, ct);
        return saved.IsFailure ? saved.Error : link.Value!.Id;
    }
}

internal sealed class UnlinkSiteDocumentCommandHandler(SitesDbContext db)
    : ICommandHandler<UnlinkSiteDocumentCommand>
{
    public async Task<Result> HandleAsync(
        UnlinkSiteDocumentCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        if (!site.IsEditable)
        {
            return SiteErrors.SiteLocked;
        }

        SiteDocument? link = await db.SiteDocuments
            .FirstOrDefaultAsync(d => d.Id == command.DocumentLinkId, ct);

        if (link is null)
        {
            return SiteErrors.DocumentNotFound;
        }

        // Not-found rather than forbidden: a caller reaching the wrong site is not told the link exists.
        bool belongs = link.SiteId == command.SiteId
                      || link.RecordId is { } recordId
                          && await db.SiteRecords.AnyAsync(
                              r => r.Id == recordId && r.SiteId == command.SiteId, ct);

        if (!belongs)
        {
            return SiteErrors.DocumentNotFound;
        }

        // Unlinks only. The document and its bytes keep their own lifecycle.
        link.Deactivate();

        return await SiteWrites.SaveAsync(db, ct);
    }
}

/// <summary>
/// A survey's attachments — its own, plus those pinned to any of its records, so the gallery is one call
/// rather than one per record.
/// </summary>
internal sealed class GetSiteDocumentsQueryHandler(SitesDbContext db)
    : IQueryHandler<GetSiteDocumentsQuery, IReadOnlyList<SiteDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<SiteDocumentResponse>>> HandleAsync(
        GetSiteDocumentsQuery query, CancellationToken ct = default)
    {
        IQueryable<Guid> recordIds = db.SiteRecords.AsNoTracking()
            .Where(r => r.SiteId == query.SiteId)
            .Select(r => r.Id);

        IReadOnlyList<SiteDocumentResponse> documents = await db.SiteDocuments.AsNoTracking()
            .Where(d => d.SiteId == query.SiteId
                        || d.RecordId != null && recordIds.Contains(d.RecordId.Value))
            .OrderBy(d => d.Kind).ThenBy(d => d.CreatedAtUtc)
            .Select(d => new SiteDocumentResponse(
                d.Id, d.DocumentId, d.SiteId, d.RecordId,
                SiteEnumMapping.ToContracts(d.Kind), d.AddedById, d.CreatedAtUtc))
            .ToListAsync(ct);

        return Result.Success(documents);
    }
}
