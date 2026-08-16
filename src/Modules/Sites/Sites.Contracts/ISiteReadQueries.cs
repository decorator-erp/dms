using Sites.Contracts.Records;

namespace Sites.Contracts;

/// <summary>
/// The two derived reads that cannot be expressed in the ORM, ported from the backend's
/// <c>ISiteReadQueries</c>. Both are hand-written SQL over the sites schema (families uses
/// <c>unnest(… ) WITH ORDINALITY</c> and a tolerance-snapped <c>string_agg</c> signature; BOQ uses
/// <c>DISTINCT ON</c> and filtered aggregates), and neither has a LINQ equivalent worth the attempt.
///
/// <para>Deliberately a NARROW port rather than exposing the DbContext's <c>DatabaseFacade</c> — the
/// backend's own reasoning, and it holds here too: raw SQL bypasses EF's global query filters, so
/// every statement behind this interface must re-state the soft-delete guard itself
/// (<c>JOIN sites s ON s.id = r.site_id AND s.is_active</c>). Keeping the surface to two methods keeps
/// that obligation in one reviewable place.</para>
///
/// <para>Module-internal: implemented in <c>Sites.Infrastructure</c> and consumed by
/// <c>Sites.Application</c>, never by another module. It lives in Contracts for the same structural
/// reason <c>Crm.Contracts.ICrmSchedulerService</c> does — <c>Sites.Application</c> cannot name an
/// <c>internal</c> type across the assembly boundary, and Contracts is the one project both layers
/// already reference without a cycle.</para>
/// </summary>
public interface ISiteReadQueries
{
    Task<IReadOnlyList<SiteFamilyResponse>> GetFamiliesAsync(
        Guid siteId, Guid moduleId, IReadOnlyList<Guid> groupByItemIds, CancellationToken ct = default);

    Task<IReadOnlyList<SiteBoqLineResponse>> GetBoqAsync(Guid siteId, CancellationToken ct = default);
}
