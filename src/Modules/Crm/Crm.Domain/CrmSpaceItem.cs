using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmSpaceItem.cs</c> — one row of the working board.
/// Either a catalog item recorded in this space (<see cref="CatalogItemId"/>) or a typed custom entry
/// under a category (<see cref="CustomName"/> + <see cref="CustomCategoryId"/>) — exactly one shape,
/// enforced by the two factories. The board is office working data, not part of the approval ledger —
/// <see cref="Status"/> flips freely.
/// </summary>
internal sealed class CrmSpaceItem : Entity, IAuditable
{
    public Guid SpaceId { get; private set; }
    public Guid? CatalogItemId { get; private set; }
    public string? CustomName { get; private set; }
    public Guid? CustomCategoryId { get; private set; }
    public CrmProvisionStatus Status { get; private set; }
    public string? Note { get; private set; }
    public Guid? SourceBriefSpaceId { get; private set; }

    public bool IsCustom => CatalogItemId is null;

    private CrmSpaceItem() { }

    public static Result<CrmSpaceItem> CreateFromItem(
        Guid spaceId, Guid catalogItemId, CrmProvisionStatus status = CrmProvisionStatus.ToBuy,
        string? note = null, Guid? sourceBriefSpaceId = null)
    {
        if (spaceId == Guid.Empty) return CrmSpaceErrors.SpaceNotFound;
        if (catalogItemId == Guid.Empty) return CrmCatalogErrors.ItemNotFound;

        return new CrmSpaceItem
        {
            SpaceId = spaceId,
            CatalogItemId = catalogItemId,
            Status = status,
            Note = note,
            SourceBriefSpaceId = sourceBriefSpaceId,
        };
    }

    public static Result<CrmSpaceItem> CreateCustom(
        Guid spaceId, Guid customCategoryId, string customName,
        CrmProvisionStatus status = CrmProvisionStatus.ToBuy, string? note = null,
        Guid? sourceBriefSpaceId = null)
    {
        if (spaceId == Guid.Empty) return CrmSpaceErrors.SpaceNotFound;
        if (customCategoryId == Guid.Empty) return CrmCatalogErrors.CategoryNotFound;
        if (string.IsNullOrWhiteSpace(customName)) return CrmCatalogErrors.CustomNameRequired;

        return new CrmSpaceItem
        {
            SpaceId = spaceId,
            CustomCategoryId = customCategoryId,
            CustomName = customName.Trim(),
            Status = status,
            Note = note,
            SourceBriefSpaceId = sourceBriefSpaceId,
        };
    }

    /// <summary>Converts a recurring custom entry into a reference to a real catalog item: sets the
    /// FK and clears both custom columns, so the row swaps shape in one step. The caller (G9-11's
    /// Application handler) validates the item's category matches or descends from
    /// <see cref="CustomCategoryId"/> before calling this — Domain has no category-tree access.</summary>
    public Result PromoteToItem(Guid catalogItemId)
    {
        if (!IsCustom) return CrmCatalogErrors.NotACustomEntry;
        if (catalogItemId == Guid.Empty) return CrmCatalogErrors.ItemNotFound;

        CatalogItemId = catalogItemId;
        CustomName = null;
        CustomCategoryId = null;
        return Result.Success();
    }

    public Result Rename(string customName)
    {
        if (!IsCustom) return CrmCatalogErrors.NotACustomEntry;
        if (string.IsNullOrWhiteSpace(customName)) return CrmCatalogErrors.CustomNameRequired;

        CustomName = customName.Trim();
        return Result.Success();
    }

    public void SetStatus(CrmProvisionStatus status, string? note)
    {
        Status = status;
        Note = note;
    }

    public void SetSourceBriefSpace(Guid? sourceBriefSpaceId) => SourceBriefSpaceId = sourceBriefSpaceId;

    public void Remove() => SetActive(false);

    public void Restore() => SetActive(true);
}
