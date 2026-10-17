using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

/// <summary>The shared value-shape rule engine, unit-tested hardest per <c>PLAN-PHASE4.md</c>'s
/// instruction — every (declared type × populated column) combination, not just the happy path per
/// type. Exercised indirectly through both consuming entities in the other test files; this file
/// targets the rule itself, in full, at both call sites' shared boundary.</summary>
public class CatalogAttributeValueRulesTests
{
    // xunit test methods must be public, but CatalogAttributeType is internal (arch rule 4) - a
    // public method can't expose a less-accessible parameter type (CS0051), so theory data travels as
    // int and is cast back to the enum inside the method body, same pattern as Projects.UnitTests.
    [Theory]
    [InlineData((int)CatalogAttributeType.Text)]
    [InlineData((int)CatalogAttributeType.Number)]
    [InlineData((int)CatalogAttributeType.Boolean)]
    [InlineData((int)CatalogAttributeType.Date)]
    [InlineData((int)CatalogAttributeType.Select)]
    public void Validate_WithNoValuePopulated_FailsForEveryType(int typeValue)
    {
        Result result = CatalogAttributeValueRules.Validate((CatalogAttributeType)typeValue, null, null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Validate_Text_WithTextPopulated_Succeeds()
    {
        Result result = CatalogAttributeValueRules.Validate(CatalogAttributeType.Text, "matte", null, null, null, null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Text_WithWhitespaceOnly_TreatedAsAbsent()
    {
        // HasText trims/whitespace-checks, so a whitespace-only string is "not a value" — matching
        // Text's own factory-side trimming behavior, this must fail as unpopulated, not "wrong type".
        Result result = CatalogAttributeValueRules.Validate(CatalogAttributeType.Text, "   ", null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Validate_Number_WithNumberPopulated_Succeeds()
    {
        Result result = CatalogAttributeValueRules.Validate(CatalogAttributeType.Number, null, 42m, null, null, null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Boolean_WithBoolPopulated_Succeeds()
    {
        Result result = CatalogAttributeValueRules.Validate(CatalogAttributeType.Boolean, null, null, true, null, null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Date_WithDatePopulated_Succeeds()
    {
        Result result = CatalogAttributeValueRules.Validate(
            CatalogAttributeType.Date, null, null, null, new DateOnly(2026, 1, 1), null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Select_WithOptionIdPopulated_Succeeds()
    {
        Result result = CatalogAttributeValueRules.Validate(
            CatalogAttributeType.Select, null, null, null, null, Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData((int)CatalogAttributeType.Text)]
    [InlineData((int)CatalogAttributeType.Number)]
    [InlineData((int)CatalogAttributeType.Boolean)]
    [InlineData((int)CatalogAttributeType.Date)]
    public void Validate_NonSelectType_WithOptionIdInstead_Fails(int typeValue)
    {
        // The populated column must be the one THIS type dictates, not merely any one column - this
        // is the "half a Postgres CHECK can't see" this rule exists for.
        Result result = CatalogAttributeValueRules.Validate((CatalogAttributeType)typeValue, null, null, null, null, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Validate_Select_WithTextInstead_Fails()
    {
        Result result = CatalogAttributeValueRules.Validate(CatalogAttributeType.Select, "matte", null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Validate_WithTwoColumnsPopulated_FailsRegardlessOfWhichTypeIsDeclared()
    {
        Result result = CatalogAttributeValueRules.Validate(CatalogAttributeType.Number, "text too", 42m, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Validate_WithAllFiveColumnsPopulated_Fails()
    {
        Result result = CatalogAttributeValueRules.Validate(
            CatalogAttributeType.Text, "a", 1m, true, new DateOnly(2026, 1, 1), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HasText_ReturnsFalse_ForNullEmptyOrWhitespace(string? value) => Assert.False(CatalogAttributeValueRules.HasText(value));

    [Fact]
    public void HasText_ReturnsTrue_ForNonBlankValue() => Assert.True(CatalogAttributeValueRules.HasText("matte"));
}
