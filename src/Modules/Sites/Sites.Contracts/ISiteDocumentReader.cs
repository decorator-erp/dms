namespace Sites.Contracts;

/// <summary>P2-A: one active survey attachment link (<c>sites.site_documents</c>) resolved to the document it names
/// and the project it belongs to. <see cref="SiteId"/> is the survey even for a record-level link.</summary>
public sealed record SiteDocumentSummary(
    Guid Id, Guid SiteId, Guid? RecordId, Guid ProjectId, Guid DocumentId, SiteDocumentKind Kind, string SiteCode);

/// <summary>
/// P2-A: lets CRM's client portal resolve a shared site photo to its document and project without reading this
/// module's tables. Only live rows count: an unlinked attachment, a soft-deleted record or a soft-deleted survey
/// resolves to nothing, so a share of it disappears from the portal.
/// </summary>
public interface ISiteDocumentReader
{
    Task<SiteDocumentSummary?> GetAsync(Guid siteDocumentId, CancellationToken ct);

    Task<IReadOnlyList<SiteDocumentSummary>> GetManyAsync(IReadOnlyCollection<Guid> siteDocumentIds, CancellationToken ct);
}
