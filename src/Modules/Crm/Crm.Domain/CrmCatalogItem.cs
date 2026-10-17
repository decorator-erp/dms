using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmCatalogItem.cs</c> — the thing itself ("Bed M-01"),
/// under exactly one <see cref="CrmCatalogCategory"/>. A leaf by construction: no path, no children,
/// no selection rule. Its own spec lives in <see cref="CrmCatalogItemAttributeValue"/> rows.
/// </summary>
internal sealed class CrmCatalogItem : Entity, IAuditable
{
    public Guid CategoryId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string? DescriptionEn { get; private set; }
    public string? DescriptionAr { get; private set; }
    public string? ImageUri { get; private set; }

    /// <summary>Finishes and labour aren't SKUs; the buy list only wants the purchasable ones.</summary>
    public bool IsPurchasable { get; private set; }

    public int SortOrder { get; private set; }

    private CrmCatalogItem() { }

    public static Result<CrmCatalogItem> Create(
        Guid categoryId, string code, string nameEn, string nameAr, bool isPurchasable = true,
        int sortOrder = 0, string? descriptionEn = null, string? descriptionAr = null, string? imageUri = null)
    {
        Result validation = Validate(categoryId, code, nameEn, nameAr);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new CrmCatalogItem
        {
            CategoryId = categoryId,
            Code = code,
            NameEn = nameEn,
            NameAr = nameAr,
            IsPurchasable = isPurchasable,
            SortOrder = sortOrder,
            DescriptionEn = descriptionEn,
            DescriptionAr = descriptionAr,
            ImageUri = imageUri,
        };
    }

    public Result Update(
        string code, string nameEn, string nameAr, bool isPurchasable,
        string? descriptionEn, string? descriptionAr, string? imageUri)
    {
        Result validation = Validate(CategoryId, code, nameEn, nameAr);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        IsPurchasable = isPurchasable;
        DescriptionEn = descriptionEn;
        DescriptionAr = descriptionAr;
        ImageUri = imageUri;
        return Result.Success();
    }

    /// <summary>Re-files the item under another category. The caller (Application) re-checks the
    /// item's attribute values against the new effective set — the inherited attributes change with
    /// the category, so a move can strand a value that no longer applies. Domain enforces only the
    /// shape here, not the cross-aggregate consequence.</summary>
    public Result MoveToCategory(Guid categoryId)
    {
        if (categoryId == Guid.Empty)
        {
            return CrmCatalogErrors.CategoryNotFound;
        }

        CategoryId = categoryId;
        return Result.Success();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private static Result Validate(Guid categoryId, string code, string nameEn, string nameAr)
    {
        if (categoryId == Guid.Empty)
        {
            return CrmCatalogErrors.CategoryNotFound;
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            return CrmCatalogErrors.InvalidData;
        }

        return Result.Success();
    }
}
