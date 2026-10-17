using SharedKernel;

namespace Sites.Domain;

/// <summary>
/// One captured value: what a <see cref="SiteRecord"/> measured for one <c>SiteItem</c>.
///
/// <para><b>Deliberately not an <see cref="Entity"/>.</b> Its identity is the composite key
/// <c>(RecordId, ItemId)</c> — a record has at most one value per item, which the key states
/// structurally rather than leaving to a unique index over a surrogate id. It therefore carries no
/// <c>Id</c>, no <c>IsActive</c> and no audit columns. This is the same join-row exemption
/// <c>Hr.Domain.EmployeeDepartment</c> already takes, per <c>docs/adding-a-module.md</c>'s
/// "deriving from <c>SharedKernel.Entity</c> unless they're a pure join/association row" rule.</para>
///
/// <para>Two consequences the rest of the module absorbs: reads cannot filter <c>IsActive</c> here, so
/// they filter the parent <see cref="SiteRecord"/>'s flag instead; and clearing a value is a hard
/// delete, not a soft flag — which is correct, a cleared measurement is not a measurement.</para>
///
/// <para>Exactly one value column is populated, enforced here and by a check constraint. That the
/// populated one MATCHES the item's declared data type is a domain invariant and cannot be a
/// constraint — a CHECK cannot read another row.</para>
/// </summary>
internal sealed class SiteRecordValue
{
    public Guid RecordId { get; private set; }
    public Guid ItemId { get; private set; }

    public decimal? ValueNumber { get; private set; }
    public string? ValueText { get; private set; }
    public bool? ValueBool { get; private set; }

    /// <summary><c>date</c>, not <c>timestamptz</c>: a measured date has no time-of-day, and
    /// <see cref="DateOnly"/> sidesteps the global UTC converter.</summary>
    public DateOnly? ValueDate { get; private set; }

    public Guid? OptionId { get; private set; }

    private SiteRecordValue() { }

    public static Result<SiteRecordValue> Create(
        Guid recordId, Guid itemId, SiteItemDataType dataType,
        decimal? valueNumber = null, string? valueText = null, bool? valueBool = null,
        DateOnly? valueDate = null, Guid? optionId = null)
    {
        if (recordId == Guid.Empty || itemId == Guid.Empty)
        {
            return SiteErrors.InvalidData;
        }

        // Whitespace-only text is not a value. Normalizing BEFORE counting is what makes "   " count as
        // unpopulated rather than as a populated Text value that later reads as blank.
        string? text = string.IsNullOrWhiteSpace(valueText) ? null : valueText.Trim();

        int populated = (valueNumber.HasValue ? 1 : 0)
                      + (text is not null ? 1 : 0)
                      + (valueBool.HasValue ? 1 : 0)
                      + (valueDate.HasValue ? 1 : 0)
                      + (optionId.HasValue ? 1 : 0);

        if (populated != 1)
        {
            return SiteErrors.InvalidData;
        }

        bool matches = dataType switch
        {
            SiteItemDataType.Number => valueNumber.HasValue,
            SiteItemDataType.Text => text is not null,
            SiteItemDataType.Bool => valueBool.HasValue,
            SiteItemDataType.Select => optionId.HasValue,
            SiteItemDataType.Date => valueDate.HasValue,
            _ => false,
        };
        if (!matches)
        {
            return SiteErrors.ValueTypeMismatch;
        }

        return new SiteRecordValue
        {
            RecordId = recordId,
            ItemId = itemId,
            ValueNumber = valueNumber,
            ValueText = text,
            ValueBool = valueBool,
            ValueDate = valueDate,
            OptionId = optionId,
        };
    }
}
