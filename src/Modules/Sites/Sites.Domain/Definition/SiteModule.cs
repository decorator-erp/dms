using SharedKernel;

namespace Sites.Domain.Definition;

/// <summary>
/// A named group of fields on the survey form — and, more importantly, <i>a kind of element the
/// surveyor enumerates</i>. That is what makes a <see cref="SiteRecord"/> a countable thing and what
/// makes the families rollup possible at all: "openings" is not a section of one big form, it is the
/// set of openings in the unit.
///
/// <para><b>Global master data — deliberately no project id, and deliberately not
/// <c>ISiteScoped</c>.</b> The schema is read by module <b>code</b> and cached under it, which a
/// per-project definition could not be keyed by. Every command over this type is gated by permission
/// alone (<c>sites.definitions.manage</c>), never by row-level access. See
/// <c>Sites.Contracts.ISiteScoped</c> for why that exemption is machine-checked rather than
/// trusted.</para>
/// </summary>
internal sealed class SiteModule : Entity, IAuditable
{
    /// <summary>Unique case-insensitively. The cache key of the schema endpoint.</summary>
    public string Code { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;

    /// <summary>Optional helper text shown above the module's grid on the tablet.</summary>
    public string? DescriptionEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    /// <summary>
    /// One of the seeded modules. Blocks deletion and code edits — the seeded codes are referenced by
    /// the pivot, the families groupBy and the BOQ rollup, so losing one silently empties those reads.
    /// Enforced here and in the handler: a CHECK constraint cannot veto a DELETE, and this repo has no
    /// triggers.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>Tab order on the survey form.</summary>
    public int SortOrder { get; private set; }

    private SiteModule() { }

    public static Result<SiteModule> Create(
        string code, string nameEn, string nameAr,
        string? descriptionEn = null, string? descriptionAr = null,
        int sortOrder = 0, bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(nameEn)
            || string.IsNullOrWhiteSpace(nameAr))
            return SiteErrors.InvalidData;

        // Divergence from the backend, which range-checks sortOrder on SiteRecord and SiteModuleItem but
        // not here or on SiteItemOption. A negative tab order is as meaningless as a negative field
        // order; the asymmetry was an oversight, not a rule. See this task's PROGRESS.md entry.
        if (sortOrder < 0) return SiteErrors.InvalidData;

        return new SiteModule
        {
            Code = code.Trim(),
            NameEn = nameEn.Trim(),
            NameAr = nameAr.Trim(),
            DescriptionEn = Normalize(descriptionEn),
            DescriptionAr = Normalize(descriptionAr),
            SortOrder = sortOrder,
            IsSystem = isSystem,
        };
    }

    /// <summary>
    /// Full replacement of the editable fields; omitting a description clears it, the convention the CRM
    /// PUT surfaces already state. <see cref="Code"/> is not editable here — see
    /// <see cref="ChangeCode"/>.
    /// </summary>
    public Result Update(
        string nameEn, string nameAr, string? descriptionEn, string? descriptionAr, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
            return SiteErrors.InvalidData;
        if (sortOrder < 0) return SiteErrors.InvalidData;

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        DescriptionEn = Normalize(descriptionEn);
        DescriptionAr = Normalize(descriptionAr);
        SortOrder = sortOrder;
        return Result.Success();
    }

    /// <summary>
    /// Renaming the code. Refused on a system module, whose code is referenced by the pivot, the
    /// families groupBy and the BOQ rollup.
    ///
    /// <para><b>Divergence: the error is <see cref="SiteErrors.SystemDefinitionNotRenameable"/>, not
    /// <see cref="SiteErrors.SystemModuleNotDeletable"/>.</b> The backend answers a rename with an error
    /// whose message reads "System modules cannot be deleted." — right refusal, wrong words, and the
    /// caller is told about an operation they did not attempt. Fixed rather than ported.</para>
    /// </summary>
    public Result ChangeCode(string code)
    {
        if (IsSystem) return SiteErrors.SystemDefinitionNotRenameable;
        if (string.IsNullOrWhiteSpace(code)) return SiteErrors.InvalidData;

        Code = code.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Whether this module may be hard-deleted on its own terms. The other half of the rule — whether any
    /// record references it — needs a query and lives in the handler.
    /// </summary>
    public Result EnsureDeletable() =>
        IsSystem ? SiteErrors.SystemModuleNotDeletable : Result.Success();

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
