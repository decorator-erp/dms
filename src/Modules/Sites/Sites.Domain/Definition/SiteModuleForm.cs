using SharedKernel;

namespace Sites.Domain.Definition;

/// <summary>
/// Pure operations over a module's loaded link set. The only I/O a caller does is loading the links and
/// saving them; everything between is here.
///
/// <para>This is the pure core of the backend's <c>SiteDomainService.ReorderLinksAsync</c> — the largest
/// genuinely pure block in that 485-line class. Only its opening "load this module's links" call is
/// orchestration; the membership check, the range check and the assignment loop are all computation over
/// an in-memory collection.</para>
/// </summary>
internal static class SiteModuleForm
{
    /// <summary>
    /// Applies a new field order to <paramref name="links"/>.
    ///
    /// <para><b>Validate-then-apply, which the backend does not do (defect B19).</b> Its version calls
    /// <c>SetSortOrder</c> inside the loop and can still fail on a later entry, leaving a partial reorder
    /// on the change tracker. Nothing persists today only because the handler returns before saving and
    /// the scoped DbContext dies with the request — the correctness depends on an accident of lifetime,
    /// not on the code. Any future decorator, retry, or second save in the same request would flush the
    /// half-applied order. Here nothing is mutated until every entry is known good.</para>
    ///
    /// <para>Inactive links are included by the caller deliberately: reordering must be able to position
    /// a detached field so that re-attaching it lands it where the admin left it.</para>
    /// </summary>
    /// <param name="links">The module's links, active and inactive.</param>
    /// <param name="order">Requested (item id, sort order) pairs. Items absent from this list keep their
    /// current order.</param>
    public static Result Reorder(
        IReadOnlyCollection<SiteModuleItem> links,
        IReadOnlyCollection<(Guid ItemId, int SortOrder)> order)
    {
        var byItemId = links.ToDictionary(l => l.ItemId);

        // Pass 1 -- prove the whole request is applicable. No mutation yet.
        foreach ((Guid itemId, int sortOrder) in order)
        {
            if (!byItemId.ContainsKey(itemId))
            {
                return SiteErrors.LinkNotFound;
            }

            if (sortOrder < 0)
            {
                return SiteErrors.InvalidData;
            }
        }

        // Pass 2 -- apply. Cannot fail: every key resolved above and every value is in range.
        foreach ((Guid itemId, int sortOrder) in order)
        {
            byItemId[itemId].SetSortOrder(sortOrder);
        }

        return Result.Success();
    }
}
