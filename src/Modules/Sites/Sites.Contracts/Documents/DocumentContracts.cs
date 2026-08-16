using BuildingBlocks.Messaging;

namespace Sites.Contracts.Documents;

// ── Requests / responses ───────────────────────────────────────────────────────

/// <summary>
/// Links an EXISTING <c>documents.project_documents</c> row to a site or one of its records. Sites
/// stores no file and introduces no storage path — it mirrors <c>CrmArtifactDocument</c> exactly, so
/// there is no <c>IFileStorageService</c> reference and no upload command anywhere in this module.
///
/// <para><see cref="RecordId"/> null means the attachment belongs to the site as a whole; set means it
/// belongs to that record. Exactly one subject, enforced by the entity and by a check constraint.</para>
/// </summary>
public sealed record LinkSiteDocumentRequest(
    Guid DocumentId, SiteDocumentKind Kind, Guid? RecordId = null);

public sealed record SiteDocumentResponse(
    Guid Id, Guid DocumentId, Guid? SiteId, Guid? RecordId, SiteDocumentKind Kind,
    Guid AddedById, DateTime CreatedAtUtc);

// ── Commands ───────────────────────────────────────────────────────────────────

/// <summary><see cref="AddedById"/> is a pre-resolved HR employee id — see
/// <c>Sites.Contracts.Surveys.CreateSiteCommand</c>'s remarks on endpoint-side actor
/// resolution.</summary>
public sealed record LinkSiteDocumentCommand(
    Guid SiteId, LinkSiteDocumentRequest Request, Guid AddedById, string AppUserId)
    : ICommand<Guid>, ISiteScoped;

public sealed record UnlinkSiteDocumentCommand(Guid SiteId, Guid DocumentLinkId, string AppUserId)
    : ICommand, ISiteScoped;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetSiteDocumentsQuery(Guid SiteId, string AppUserId)
    : IQuery<IReadOnlyList<SiteDocumentResponse>>, ISiteScoped;
