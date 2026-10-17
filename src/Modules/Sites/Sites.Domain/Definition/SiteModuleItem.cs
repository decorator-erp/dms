using SharedKernel;

namespace Sites.Domain.Definition;

/// <summary>
/// Puts a <see cref="SiteItem"/> on a <see cref="SiteModule"/>'s form, and owns everything contextual
/// about that placement: whether it is required, whether it is drawn, and where it sits.
///
/// <para>The seed carries 18 links over 10 items — a 1.8× reuse factor, which is the whole argument for
/// this table existing. If each item belonged to one module the join would be dead weight and
/// <see cref="IsRequired"/> could live on the item. It cannot: <c>mount_height_cm</c> is required in
/// electrical points and optional in openings.</para>
/// </summary>
internal sealed class SiteModuleItem : Entity, IAuditable
{
    public Guid ModuleId { get; private set; }
    public Guid ItemId { get; private set; }

    public bool IsRequired { get; private set; }

    /// <summary>
    /// Distinct from <see cref="Entity.IsActive"/>, and both are needed. Inactive means the link is
    /// deleted — the field does not exist for this module. Not visible means the field exists, is
    /// validated and is captured, but is not drawn on the tablet. Collapsing the two would make "hidden
    /// but required" unrepresentable.
    /// </summary>
    public bool IsVisible { get; private set; }

    /// <summary>Field order within the module's form, and the pivot's column order.</summary>
    public int SortOrder { get; private set; }

    private SiteModuleItem() { }

    public static Result<SiteModuleItem> Create(
        Guid moduleId, Guid itemId, bool isRequired = false, bool isVisible = true, int sortOrder = 0)
    {
        if (moduleId == Guid.Empty || itemId == Guid.Empty)
        {
            return SiteErrors.InvalidData;
        }

        if (sortOrder < 0)
        {
            return SiteErrors.InvalidData;
        }

        return new SiteModuleItem
        {
            ModuleId = moduleId,
            ItemId = itemId,
            IsRequired = isRequired,
            IsVisible = isVisible,
            SortOrder = sortOrder,
        };
    }

    public Result Update(bool isRequired, bool isVisible, int sortOrder)
    {
        if (sortOrder < 0)
        {
            return SiteErrors.InvalidData;
        }

        IsRequired = isRequired;
        IsVisible = isVisible;
        SortOrder = sortOrder;
        return Result.Success();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>
    /// Detach is soft, and that is load-bearing: captured values survive, so re-attaching the item makes
    /// the historical data visible again rather than orphaning it. See
    /// <c>SiteModuleForm.Reorder</c>'s sibling note on reattachment.
    /// </summary>
    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
