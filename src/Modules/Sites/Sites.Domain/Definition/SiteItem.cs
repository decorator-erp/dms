using System.Text.RegularExpressions;
using SharedKernel;

namespace Sites.Domain.Definition;

/// <summary>
/// A reusable field definition, shared across modules. Global master data, like <see cref="SiteModule"/>.
///
/// <para>Note what is <b>not</b> here: <c>IsRequired</c>, <c>SortOrder</c> and visibility. Those live on
/// <see cref="SiteModuleItem"/>, because requiredness is contextual — <c>mount_height_cm</c> is required
/// in electrical points (a socket is always measured against a datum) and optional in openings (a sill
/// height applies to windows, not doors). Two homes for one flag is the defect that split avoids.</para>
/// </summary>
internal sealed class SiteItem : Entity, IAuditable
{
    /// <summary>
    /// Unique case-insensitively. This is a public contract, not a label: it is the pivot's column
    /// header, the families groupBy token and the Revit exporter's join key.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;

    public SiteItemDataType DataType { get; private set; }

    /// <summary>Display and BOQ unit — <c>cm</c>, <c>pcs</c>. Gives <see cref="Tolerance"/> its
    /// meaning.</summary>
    public string? Unit { get; private set; }

    public decimal? MinValue { get; private set; }
    public decimal? MaxValue { get; private set; }

    /// <summary>
    /// The measurement grain, <b>expressed in this item's own <see cref="Unit"/></b>. Two captures within
    /// one tolerance of each other are the same thing and must group into one family. 0 means exact.
    ///
    /// <para>Per-item rather than a global rounding because a global one is unit-blind: two decimal
    /// places is 1 cm of grain in metres and a no-op in centimetres, so a mixed-unit schema would get
    /// tolerance on half its data and none on the other half.</para>
    /// </summary>
    public decimal Tolerance { get; private set; }

    public int? MaxLength { get; private set; }

    /// <summary>Validated in .NET, never as a Postgres CHECK — the two regex dialects disagree.</summary>
    public string? RegexPattern { get; private set; }

    /// <summary>One of the seeded items; its code is referenced by the pivot, families and the BOQ.</summary>
    public bool IsSystem { get; private set; }

    private SiteItem() { }

    public static Result<SiteItem> Create(
        string code, string nameEn, string nameAr, SiteItemDataType dataType,
        string? unit = null, decimal? minValue = null, decimal? maxValue = null,
        decimal tolerance = 0m, int? maxLength = null, string? regexPattern = null,
        bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(nameEn)
            || string.IsNullOrWhiteSpace(nameAr))
            return SiteErrors.InvalidData;

        var limits = ValidateLimits(dataType, minValue, maxValue, tolerance, maxLength, regexPattern);
        if (limits.IsFailure) return limits.Error;

        return new SiteItem
        {
            Code = code.Trim(),
            NameEn = nameEn.Trim(),
            NameAr = nameAr.Trim(),
            DataType = dataType,
            Unit = Normalize(unit),
            MinValue = minValue,
            MaxValue = maxValue,
            Tolerance = tolerance,
            MaxLength = maxLength,
            RegexPattern = Normalize(regexPattern),
            IsSystem = isSystem,
        };
    }

    /// <summary>
    /// The names and the numeric limits stay editable for the life of the item — tightening a bound is a
    /// policy change, not a reinterpretation of stored data. <see cref="Code"/>, <see cref="DataType"/>,
    /// <see cref="Unit"/> and <see cref="Tolerance"/> are not editable here; see
    /// <see cref="ChangeCapturedShape"/>.
    /// </summary>
    public Result Update(
        string nameEn, string nameAr,
        decimal? minValue, decimal? maxValue, int? maxLength, string? regexPattern)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
            return SiteErrors.InvalidData;

        // Re-validated against the CURRENT DataType/Tolerance -- this method cannot change either.
        var limits = ValidateLimits(DataType, minValue, maxValue, Tolerance, maxLength, regexPattern);
        if (limits.IsFailure) return limits.Error;

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        MinValue = minValue;
        MaxValue = maxValue;
        MaxLength = maxLength;
        RegexPattern = Normalize(regexPattern);
        return Result.Success();
    }

    /// <summary>
    /// The four fields that decide how every stored value is read and grouped. The handler refuses this
    /// once any captured value references the item — changing one would re-shard the whole historical
    /// family set without a single measurement changing, which reads as data loss.
    ///
    /// <para>Note the asymmetry with <see cref="Update"/>: limits are re-validated against the NEW data
    /// type and tolerance while keeping the existing bounds, so flipping a bounded Number to Text fails
    /// with <see cref="SiteErrors.LimitNotLegalForType"/> rather than silently orphaning the bounds.</para>
    /// </summary>
    public Result ChangeCapturedShape(
        string code, SiteItemDataType dataType, string? unit, decimal tolerance)
    {
        if (string.IsNullOrWhiteSpace(code)) return SiteErrors.InvalidData;

        var limits = ValidateLimits(dataType, MinValue, MaxValue, tolerance, MaxLength, RegexPattern);
        if (limits.IsFailure) return limits.Error;

        Code = code.Trim();
        DataType = dataType;
        Unit = Normalize(unit);
        Tolerance = tolerance;
        return Result.Success();
    }

    /// <summary>
    /// Retiring an item hides it from every form without touching the values already captured against
    /// it — which is why deactivation is the answer <see cref="SiteErrors.ItemInUse"/> points callers
    /// at when deletion is refused.
    /// </summary>
    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);

    /// <summary>True when <paramref name="code"/> is this item's code, ignoring case and surrounding
    /// space — lets a handler skip the uniqueness probe when a "rename" changes nothing.</summary>
    public bool CodeEquals(string? code) =>
        string.Equals(Code, code?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this item may be hard-deleted on its own terms. The other halves of the rule — captured
    /// values, and existing module links — need queries and live in the handler.
    /// </summary>
    public Result EnsureDeletable() =>
        IsSystem ? SiteErrors.SystemItemNotDeletable : Result.Success();

    /// <summary>
    /// Bounds, text length and option membership for one captured value.
    ///
    /// <para><b>This is the backend's <c>SiteDomainService.ValidateAgainstItem</c>, moved onto the
    /// entity.</b> It was already <c>private static</c> and documented as "synchronous: the options
    /// arrive preloaded, so nothing here touches the database" — a pure rule about one item that had been
    /// parked in an orchestration class because that is where its caller lived. It is the only part of
    /// that 485-line service that is genuinely domain logic.</para>
    ///
    /// <para>The declared-TYPE check is deliberately not here: it lives in
    /// <c>SiteRecordValue.Create</c>, which cannot construct a mismatched value in the first place. This
    /// adds only what needs the item's limits and its option list.</para>
    ///
    /// <para><paramref name="optionIdsForThisItem"/> is passed in rather than navigated to, because the
    /// caller batches option loading across every item in a grid save. Null means "this item has no
    /// options", which for a Select item makes any option id invalid.</para>
    /// </summary>
    public Result ValidateValue(
        decimal? valueNumber, string? valueText, Guid? optionId,
        IReadOnlySet<Guid>? optionIdsForThisItem)
    {
        if (valueNumber is { } number)
        {
            if (MinValue is { } min && number < min) return SiteErrors.InvalidData;
            if (MaxValue is { } max && number > max) return SiteErrors.InvalidData;
        }

        if (valueText is { } text)
        {
            var trimmed = text.Trim();

            if (MaxLength is { } maxLength && trimmed.Length > maxLength) return SiteErrors.InvalidData;

            if (RegexPattern is { } pattern && !SafeIsMatch(trimmed, pattern))
                return SiteErrors.InvalidData;
        }

        if (optionId is { } chosen)
        {
            // Also structurally guaranteed by the composite (item_id, option_id) foreign key; checked
            // here so the caller gets a 400 naming the item rather than a constraint violation.
            if (optionIdsForThisItem is null || !optionIdsForThisItem.Contains(chosen))
                return SiteErrors.OptionNotOfItem;
        }

        return Result.Success();
    }

    /// <summary>
    /// Mirrors the database's own range constraint so a rejected write fails as a 400 from here rather
    /// than a 500 from Postgres. Each limit family is legal on exactly one data type — a bound on a
    /// Select would be silently ignored by the validator, leaving its author believing a rule was in
    /// force.
    /// </summary>
    private static Result ValidateLimits(
        SiteItemDataType dataType, decimal? minValue, decimal? maxValue,
        decimal tolerance, int? maxLength, string? regexPattern)
    {
        if (minValue.HasValue && maxValue.HasValue && minValue > maxValue)
            return SiteErrors.InvalidData;

        // Null matches neither pattern, so an absent bound passes. 1024 is the value_text column's own
        // ceiling -- a larger promise would be broken by the column, as a 500, rather than here as a 400.
        if (maxLength is < 1 or > 1024) return SiteErrors.InvalidData;

        if (tolerance < 0) return SiteErrors.InvalidData;

        if (dataType != SiteItemDataType.Number
            && (minValue.HasValue || maxValue.HasValue || tolerance != 0))
            return SiteErrors.LimitNotLegalForType;

        if (dataType != SiteItemDataType.Text
            && (maxLength.HasValue || !string.IsNullOrWhiteSpace(regexPattern)))
            return SiteErrors.LimitNotLegalForType;

        return Result.Success();
    }

    /// <summary>
    /// Timed out, and a malformed pattern treated as "no pattern" — <see cref="RegexPattern"/> is
    /// admin-supplied through the item definition and this runs on the surveyor's write path, so neither
    /// a catastrophic backtrack nor a typo in the form builder may take down a capture. Both failure
    /// modes deliberately PASS the value rather than reject it: refusing would let one bad pattern block
    /// every submission for that field.
    /// </summary>
    private static bool SafeIsMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
