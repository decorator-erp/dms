using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmCatalogItemAttributeValueTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = CrmCatalogItemAttributeValue.Create(
            Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Number, valueNumber: 200);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.Value!.ValueNumber);
    }

    [Fact]
    public void Create_WithEmptyItemId_Fails()
    {
        var result = CrmCatalogItemAttributeValue.Create(
            Guid.Empty, Guid.NewGuid(), CatalogAttributeType.Number, valueNumber: 200);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public void Create_WithEmptyAttributeId_Fails()
    {
        var result = CrmCatalogItemAttributeValue.Create(
            Guid.NewGuid(), Guid.Empty, CatalogAttributeType.Number, valueNumber: 200);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeNotFound, result.Error);
    }

    [Fact]
    public void Create_TrimsTextValue()
    {
        var result = CrmCatalogItemAttributeValue.Create(
            Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Text, valueText: "  Modern  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Modern", result.Value!.ValueText);
    }

    [Fact]
    public void Create_WithMismatchedType_Fails()
    {
        var result = CrmCatalogItemAttributeValue.Create(
            Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Boolean, valueText: "true");

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }
}
