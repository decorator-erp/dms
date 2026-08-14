using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmCatalogCategory.cs</c> — one node of the catalog tree,
/// a kind of thing ("Beds"), never a thing itself. Carries the branch rules its subtree obeys
/// (<see cref="SelectionMode"/>/<see cref="IsRequired"/>) and inherits attributes to every descendant
/// category and their items.
///
/// <para><see cref="Path"/>/<see cref="Depth"/> are derived state — only the Application handler that
/// owns the subtree rewrite (G9-11, mirroring the backend's <c>CrmCatalogCategoryDomainService</c>)
/// may write them, via <see cref="SetPath"/>/<see cref="SetParent"/>, both kept `internal`.</para>
/// </summary>
internal sealed class CrmCatalogCategory : Entity, IAuditable
{
    /// <summary>Each level costs 37 characters of <see cref="Path"/> (36-char guid + '/'), so 27
    /// levels is the most a <c>varchar(1024)</c> holds.</summary>
    public const int MaxDepth = 26;

    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string? DescriptionEn { get; private set; }
    public string? DescriptionAr { get; private set; }
    public string? ImageUri { get; private set; }
    public CatalogSelectionMode SelectionMode { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>Materialized path of ids — <c>/{ancestor}/…/{own id}/</c>, always leading and trailing
    /// slash so <c>LIKE path || '%'</c> matches descendants and never a sibling with a shared prefix.</summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>Root = 0.</summary>
    public int Depth { get; private set; }

    private CrmCatalogCategory() { }

    public static Result<CrmCatalogCategory> Create(
        Guid? parentId, string code, string nameEn, string nameAr,
        CatalogSelectionMode selectionMode = CatalogSelectionMode.None, bool isRequired = false,
        int sortOrder = 0, string? descriptionEn = null, string? descriptionAr = null, string? imageUri = null)
    {
        var validation = Validate(code, nameEn, nameAr);
        if (validation.IsFailure) return validation.Error;

        return new CrmCatalogCategory
        {
            ParentId = parentId,
            Code = code,
            NameEn = nameEn,
            NameAr = nameAr,
            SelectionMode = selectionMode,
            IsRequired = isRequired,
            SortOrder = sortOrder,
            DescriptionEn = descriptionEn,
            DescriptionAr = descriptionAr,
            ImageUri = imageUri,
        };
    }

    /// <summary>Reparenting is the Application handler's <c>MoveAsync</c>-equivalent — it also
    /// rewrites the subtree via <see cref="SetPath"/>.</summary>
    public Result Update(
        string code, string nameEn, string nameAr, CatalogSelectionMode selectionMode, bool isRequired,
        string? descriptionEn, string? descriptionAr, string? imageUri)
    {
        var validation = Validate(code, nameEn, nameAr);
        if (validation.IsFailure) return validation.Error;

        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        SelectionMode = selectionMode;
        IsRequired = isRequired;
        DescriptionEn = descriptionEn;
        DescriptionAr = descriptionAr;
        ImageUri = imageUri;
        return Result.Success();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    internal void SetPath(string path, int depth)
    {
        Path = path;
        Depth = depth;
    }

    internal void SetParent(Guid? parentId) => ParentId = parentId;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private static Result Validate(string code, string nameEn, string nameAr)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
            return CrmCatalogErrors.InvalidData;
        return Result.Success();
    }
}
