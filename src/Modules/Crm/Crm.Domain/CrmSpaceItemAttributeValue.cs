using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmSpaceItemAttributeValue.cs</c> — the per-room decision
/// for one inherited catalog attribute on one board item. Overrides the item's own catalog-level value
/// (G9-05's <c>CrmCatalogItemAttributeValue</c>); write a row here only when the room actually differs.
/// Corrections are replace-all per item — there is no update method.
/// </summary>
internal sealed class CrmSpaceItemAttributeValue : Entity, IAuditable
{
    public Guid SpaceItemId { get; private set; }
    public Guid AttributeId { get; private set; }
    public string? ValueText { get; private set; }
    public decimal? ValueNumber { get; private set; }
    public bool? ValueBool { get; private set; }
    public DateOnly? ValueDate { get; private set; }
    public Guid? OptionId { get; private set; }

    private CrmSpaceItemAttributeValue() { }

    public static Result<CrmSpaceItemAttributeValue> Create(
        Guid spaceItemId,
        Guid attributeId,
        CatalogAttributeType dataType,
        string? valueText = null,
        decimal? valueNumber = null,
        bool? valueBool = null,
        DateOnly? valueDate = null,
        Guid? optionId = null)
    {
        if (spaceItemId == Guid.Empty)
        {
            return CrmSpaceErrors.ItemNotFound;
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

        return new CrmSpaceItemAttributeValue
        {
            SpaceItemId = spaceItemId,
            AttributeId = attributeId,
            ValueText = CatalogAttributeValueRules.HasText(valueText) ? valueText!.Trim() : null,
            ValueNumber = valueNumber,
            ValueBool = valueBool,
            ValueDate = valueDate,
            OptionId = optionId,
        };
    }
}
