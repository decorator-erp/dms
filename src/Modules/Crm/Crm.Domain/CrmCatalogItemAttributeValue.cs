using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmCatalogItemAttributeValue.cs</c> — an item's own spec
/// ("Bed M-01 is Modern, 200×200"). One row per (item, attribute). This is the fallback half of the
/// two-level value model: a <see cref="CrmSpaceItemAttributeValue"/> on the board wins where present,
/// otherwise the value here applies. Corrections are replace-all per item — no update method.
/// </summary>
internal sealed class CrmCatalogItemAttributeValue : Entity, IAuditable
{
    public Guid ItemId { get; private set; }
    public Guid AttributeId { get; private set; }
    public string? ValueText { get; private set; }
    public decimal? ValueNumber { get; private set; }
    public bool? ValueBool { get; private set; }
    public DateOnly? ValueDate { get; private set; }
    public Guid? OptionId { get; private set; }

    private CrmCatalogItemAttributeValue() { }

    public static Result<CrmCatalogItemAttributeValue> Create(
        Guid itemId,
        Guid attributeId,
        CatalogAttributeType dataType,
        string? valueText = null,
        decimal? valueNumber = null,
        bool? valueBool = null,
        DateOnly? valueDate = null,
        Guid? optionId = null)
    {
        if (itemId == Guid.Empty)
        {
            return CrmCatalogErrors.ItemNotFound;
        }

        if (attributeId == Guid.Empty)
        {
            return CrmCatalogErrors.AttributeNotFound;
        }

        Result validation = CatalogAttributeValueRules.Validate(dataType, valueText, valueNumber, valueBool, valueDate, optionId);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new CrmCatalogItemAttributeValue
        {
            ItemId = itemId,
            AttributeId = attributeId,
            ValueText = CatalogAttributeValueRules.HasText(valueText) ? valueText!.Trim() : null,
            ValueNumber = valueNumber,
            ValueBool = valueBool,
            ValueDate = valueDate,
            OptionId = optionId,
        };
    }
}
