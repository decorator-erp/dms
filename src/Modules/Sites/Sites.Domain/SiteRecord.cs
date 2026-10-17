using SharedKernel;

namespace Sites.Domain;

/// <summary>
/// One captured instance within a survey — a door, a socket, a room's dimensions. The countable unit
/// that makes families and the BOQ rollup possible.
/// </summary>
internal sealed class SiteRecord : Entity, IAuditable
{
    public Guid SiteId { get; private set; }

    /// <summary>
    /// Which kind of element this is. Not optional: without it the pivot has no bounded column set,
    /// required-field validation has nothing to validate against, and families cannot be scoped.
    /// </summary>
    public Guid ModuleId { get; private set; }

    /// <summary>
    /// The room. Null means a whole-unit record — the main entrance door, the electrical panel — which
    /// the grouped read buckets under "Whole unit / عام".
    ///
    /// <para>A bare Guid into the crm schema. The space must belong to the site's own project, which no
    /// database constraint can express, so <c>Sites.Application</c> checks it through
    /// <c>Crm.Contracts.ICrmSpaceReader</c> — the contract added at S1-01 for this rule alone.</para>
    /// </summary>
    public Guid? SpaceId { get; private set; }

    /// <summary>
    /// Optional link to the reusable catalog product this row corresponds to — the Revit <i>Family</i>
    /// level. Distinct from a derived family: grouping answers "what repeats on this site", this answers
    /// "everywhere we ever installed this product", across sites and projects, which no GROUP BY over
    /// one survey can reach.
    ///
    /// <para>Unlike <see cref="SpaceId"/> this is <b>not</b> validated against CRM — the backend does not
    /// validate it either, and inventing a check the source never had is not a port. Recorded as an open
    /// question rather than silently closed; the foreign key still guarantees the row exists.</para>
    /// </summary>
    public Guid? CatalogItemId { get; private set; }

    /// <summary>Unique per (site, module), case-insensitively, among active rows.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>The surveyor's walk order.</summary>
    public int SortOrder { get; private set; }

    public string? Note { get; private set; }

    private SiteRecord() { }

    public static Result<SiteRecord> Create(
        Guid siteId, Guid moduleId, string label,
        Guid? spaceId = null, Guid? catalogItemId = null, int sortOrder = 0, string? note = null)
    {
        if (siteId == Guid.Empty || moduleId == Guid.Empty || string.IsNullOrWhiteSpace(label))
        {
            return SiteErrors.InvalidData;
        }

        if (sortOrder < 0)
        {
            return SiteErrors.InvalidData;
        }

        return new SiteRecord
        {
            SiteId = siteId,
            ModuleId = moduleId,
            Label = label.Trim(),
            SpaceId = spaceId,
            CatalogItemId = catalogItemId,
            SortOrder = sortOrder,
            Note = Normalize(note),
        };
    }

    /// <summary>
    /// The module is deliberately absent from this signature — it decides the record's whole field set,
    /// so moving a record between modules would orphan every captured value. Delete and re-create
    /// instead.
    /// </summary>
    public Result Update(string label, Guid? spaceId, Guid? catalogItemId, int sortOrder, string? note)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return SiteErrors.InvalidData;
        }

        if (sortOrder < 0)
        {
            return SiteErrors.InvalidData;
        }

        Label = label.Trim();
        SpaceId = spaceId;
        CatalogItemId = catalogItemId;
        SortOrder = sortOrder;
        Note = Normalize(note);
        return Result.Success();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private static string? Normalize(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
