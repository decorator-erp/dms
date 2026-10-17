using SharedKernel;

namespace Sites.Domain.Definition;

/// <summary>
/// One choice on a Select-typed <see cref="SiteItem"/>.
///
/// <para><see cref="Code"/>, never the display name, is what the families signature and the BOQ rollup
/// group on — so renaming an option in Arabic re-labels a family without re-sharding it.</para>
/// </summary>
internal sealed class SiteItemOption : Entity, IAuditable
{
    public Guid ItemId { get; private set; }

    /// <summary>Unique per item, case-insensitively.</summary>
    public string Code { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    private SiteItemOption() { }

    public static Result<SiteItemOption> Create(
        Guid itemId, string code, string nameEn, string nameAr, int sortOrder = 0)
    {
        if (itemId == Guid.Empty
            || string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(nameEn)
            || string.IsNullOrWhiteSpace(nameAr))
        {
            return SiteErrors.InvalidData;
        }

        // See SiteModule.Create -- the backend omits this check here and on SiteModule while enforcing it
        // on SiteRecord and SiteModuleItem. Made consistent rather than ported.
        if (sortOrder < 0)
        {
            return SiteErrors.InvalidData;
        }

        return new SiteItemOption
        {
            ItemId = itemId,
            Code = code.Trim(),
            NameEn = nameEn.Trim(),
            NameAr = nameAr.Trim(),
            SortOrder = sortOrder,
        };
    }

    /// <summary>
    /// Names and order are freely editable; <see cref="Code"/> is not, for the same reason
    /// <see cref="SiteItem.Code"/> is frozen — it is the grouping key of every historical family.
    /// </summary>
    public Result Update(string nameEn, string nameAr, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            return SiteErrors.InvalidData;
        }

        if (sortOrder < 0)
        {
            return SiteErrors.InvalidData;
        }

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        SortOrder = sortOrder;
        return Result.Success();
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
