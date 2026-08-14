using SharedKernel;

namespace Crm.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/CrmCatalogAttributeOption.cs</c> — one picklist
/// value for a <see cref="CatalogAttributeType.Select"/> attribute. Carries an image because
/// picklists are visual too (matte vs. glossy).</summary>
internal sealed class CrmCatalogAttributeOption : Entity, IAuditable
{
    public Guid AttributeId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string? ImageUri { get; private set; }
    public int SortOrder { get; private set; }

    private CrmCatalogAttributeOption() { }

    public static Result<CrmCatalogAttributeOption> Create(
        Guid attributeId, string code, string nameEn, string nameAr, string? imageUri = null, int sortOrder = 0)
    {
        var validation = Validate(code, nameEn, nameAr);
        if (validation.IsFailure) return validation.Error;

        return new CrmCatalogAttributeOption
        {
            AttributeId = attributeId,
            Code = code,
            NameEn = nameEn,
            NameAr = nameAr,
            ImageUri = imageUri,
            SortOrder = sortOrder,
        };
    }

    public Result Update(string code, string nameEn, string nameAr, string? imageUri)
    {
        var validation = Validate(code, nameEn, nameAr);
        if (validation.IsFailure) return validation.Error;

        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        ImageUri = imageUri;
        return Result.Success();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private static Result Validate(string code, string nameEn, string nameAr)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
            return CrmCatalogErrors.InvalidData;
        return Result.Success();
    }
}
