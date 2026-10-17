using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CatalogAttributeValueRules.cs</c> — the value-shape rule
/// shared by <see cref="CrmSpaceItemAttributeValue"/> (the per-room override, needed already at G9-04)
/// and G9-05's <c>CrmCatalogItemAttributeValue</c> (an item's intrinsic spec): exactly one value
/// column is populated, and it is the one the attribute's <see cref="CatalogAttributeType"/> dictates.
/// A Postgres CHECK cannot read the attribute row, so only the "exactly one" half is structural; the
/// type-vs-column half is this domain invariant.
/// </summary>
internal static class CatalogAttributeValueRules
{
    /// <summary>Whitespace is not a value — matches the trimming both callers do on the way in.</summary>
    internal static bool HasText(string? valueText) => !string.IsNullOrWhiteSpace(valueText);

    internal static Result Validate(
        CatalogAttributeType dataType,
        string? valueText,
        decimal? valueNumber,
        bool? valueBool,
        DateOnly? valueDate,
        Guid? optionId)
    {
        int populated = (HasText(valueText) ? 1 : 0)
            + (valueNumber.HasValue ? 1 : 0)
            + (valueBool.HasValue ? 1 : 0)
            + (valueDate.HasValue ? 1 : 0)
            + (optionId.HasValue ? 1 : 0);

        if (populated != 1)
        {
            return CrmCatalogErrors.AttributeTypeMismatch;
        }

        bool matches = dataType switch
        {
            CatalogAttributeType.Text => HasText(valueText),
            CatalogAttributeType.Number => valueNumber.HasValue,
            CatalogAttributeType.Boolean => valueBool.HasValue,
            CatalogAttributeType.Date => valueDate.HasValue,
            CatalogAttributeType.Select => optionId.HasValue,
            _ => false,
        };

        return matches ? Result.Success() : CrmCatalogErrors.AttributeTypeMismatch;
    }
}
