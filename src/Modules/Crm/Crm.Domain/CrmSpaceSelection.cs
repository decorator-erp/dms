using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// crm-complete F6 (D3): a storefront pick — a catalog item chosen for a space by the office, with a quantity,
/// notes, a sort order and, per attribute, the chosen option (<see cref="Options"/>). The client never writes
/// these; it sees them through the brief-space entry's snapshot (<see cref="CrmBriefSpace.Selections"/>) taken
/// when the entry is sent. Optimistic concurrency on <c>xmin</c>.
/// </summary>
internal sealed class CrmSpaceSelection : Entity, IAuditable
{
    private readonly List<CrmSpaceSelectionOption> _options = [];

    public Guid SpaceId { get; private set; }
    public Guid CatalogItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public string? Notes { get; private set; }
    public int SortOrder { get; private set; }
    public IReadOnlyList<CrmSpaceSelectionOption> Options => _options.AsReadOnly();

    private CrmSpaceSelection()
    {
        // EF materialization.
    }

    public static CrmSpaceSelection Create(Guid spaceId, Guid catalogItemId, decimal quantity, string? notes, int sortOrder) =>
        new() { SpaceId = spaceId, CatalogItemId = catalogItemId, Quantity = quantity, Notes = notes, SortOrder = sortOrder };

    public void Update(decimal quantity, string? notes)
    {
        Quantity = quantity;
        Notes = notes;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>Replaces the chosen options (one per attribute; the caller validated them).</summary>
    public void ReplaceOptions(IEnumerable<(Guid AttributeId, Guid OptionId)> options)
    {
        _options.Clear();
        _options.AddRange(options.Select(o => CrmSpaceSelectionOption.Create(Id, o.AttributeId, o.OptionId)));
    }
}

/// <summary>F6: the option chosen for one attribute of a <see cref="CrmSpaceSelection"/>.</summary>
internal sealed class CrmSpaceSelectionOption : Entity
{
    public Guid SelectionId { get; private set; }
    public Guid AttributeId { get; private set; }
    public Guid OptionId { get; private set; }

    private CrmSpaceSelectionOption()
    {
        // EF materialization.
    }

    public static CrmSpaceSelectionOption Create(Guid selectionId, Guid attributeId, Guid optionId) =>
        new() { SelectionId = selectionId, AttributeId = attributeId, OptionId = optionId };
}
