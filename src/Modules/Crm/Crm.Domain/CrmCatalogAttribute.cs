using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmCatalogAttribute.cs</c> — a typed spec declared on a
/// <see cref="CrmCatalogCategory"/> and inherited by every descendant category and their items. Its
/// picklist, when <see cref="DataType"/> is <see cref="CatalogAttributeType.Select"/>, lives in
/// <see cref="CrmCatalogAttributeOption"/>.
/// </summary>
internal sealed class CrmCatalogAttribute : Entity, IAuditable
{
    public Guid CategoryId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public CatalogAttributeType DataType { get; private set; }

    /// <summary>Display unit for a Number attribute (mm, m², W) — an annotation, never a quantity.</summary>
    public string? Unit { get; private set; }

    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }

    private CrmCatalogAttribute() { }

    public static Result<CrmCatalogAttribute> Create(
        Guid categoryId, string code, string nameEn, string nameAr, CatalogAttributeType dataType,
        string? unit = null, bool isRequired = false, int sortOrder = 0)
    {
        if (categoryId == Guid.Empty)
        {
            return CrmCatalogErrors.CategoryNotFound;
        }

        Result validation = Validate(code, nameEn, nameAr);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new CrmCatalogAttribute
        {
            CategoryId = categoryId,
            Code = code,
            NameEn = nameEn,
            NameAr = nameAr,
            DataType = dataType,
            Unit = unit,
            IsRequired = isRequired,
            SortOrder = sortOrder,
        };
    }

    /// <summary><see cref="DataType"/> is immutable — recorded values are already stored in the
    /// column that type dictates, so changing it would orphan them. Retire and redefine instead.</summary>
    public Result Update(string code, string nameEn, string nameAr, string? unit, bool isRequired)
    {
        Result validation = Validate(code, nameEn, nameAr);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        Unit = unit;
        IsRequired = isRequired;
        return Result.Success();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private static Result Validate(string code, string nameEn, string nameAr)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            return CrmCatalogErrors.InvalidData;
        }

        return Result.Success();
    }
}
