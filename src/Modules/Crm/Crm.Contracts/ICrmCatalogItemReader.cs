namespace Crm.Contracts;

/// <summary>
/// Cross-module read surface for CRM catalog items — <see cref="ICrmSpaceReader"/>'s shape. Every
/// lookup has a batch sibling, and the summary carries IDs and primitives only.
///
/// <para>Added for Sites (S2): <c>SiteRecord.CatalogItemId</c> carries a cross-schema foreign key, which
/// proves the row exists only at commit time and turns a bad id into a 500. Sites must reject a missing
/// or retired catalog item up front, and may not read <c>crm.crm_catalog_items</c> directly.
/// <see cref="CrmCatalogItemSummary.IsActive"/> is projected rather than filtered, so the caller decides
/// what a retired item means.</para>
/// </summary>
public sealed record CrmCatalogItemSummary(Guid Id, Guid CategoryId, string Code, bool IsActive);

public interface ICrmCatalogItemReader
{
    Task<CrmCatalogItemSummary?> GetAsync(Guid catalogItemId, CancellationToken ct = default);

    /// <summary>Batch, mandatory — never call <see cref="GetAsync"/> in a loop over a list.</summary>
    Task<IReadOnlyList<CrmCatalogItemSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> catalogItemIds, CancellationToken ct = default);
}
