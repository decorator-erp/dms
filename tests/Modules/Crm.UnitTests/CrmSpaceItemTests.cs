using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmSpaceItemTests
{
    [Fact]
    public void CreateFromItem_WithValidData_Succeeds()
    {
        var result = CrmSpaceItem.CreateFromItem(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsCustom);
    }

    [Fact]
    public void CreateFromItem_WithEmptySpaceId_Fails()
    {
        var result = CrmSpaceItem.CreateFromItem(Guid.Empty, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmSpaceErrors.SpaceNotFound, result.Error);
    }

    [Fact]
    public void CreateCustom_WithValidData_Succeeds()
    {
        var result = CrmSpaceItem.CreateCustom(Guid.NewGuid(), Guid.NewGuid(), "custom sofa");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsCustom);
        Assert.Equal("custom sofa", result.Value.CustomName);
    }

    [Fact]
    public void CreateCustom_WithoutName_Fails()
    {
        var result = CrmSpaceItem.CreateCustom(Guid.NewGuid(), Guid.NewGuid(), "");

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.CustomNameRequired, result.Error);
    }

    [Fact]
    public void PromoteToItem_OnCustomEntry_SwapsShape()
    {
        var item = CrmSpaceItem.CreateCustom(Guid.NewGuid(), Guid.NewGuid(), "custom sofa").Value!;
        var catalogId = Guid.NewGuid();

        var result = item.PromoteToItem(catalogId);

        Assert.True(result.IsSuccess);
        Assert.False(item.IsCustom);
        Assert.Equal(catalogId, item.CatalogItemId);
        Assert.Null(item.CustomName);
        Assert.Null(item.CustomCategoryId);
    }

    [Fact]
    public void PromoteToItem_OnCatalogEntry_Fails()
    {
        var item = CrmSpaceItem.CreateFromItem(Guid.NewGuid(), Guid.NewGuid()).Value!;

        var result = item.PromoteToItem(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.NotACustomEntry, result.Error);
    }

    [Fact]
    public void Rename_OnCatalogEntry_Fails()
    {
        var item = CrmSpaceItem.CreateFromItem(Guid.NewGuid(), Guid.NewGuid()).Value!;

        var result = item.Rename("new name");

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.NotACustomEntry, result.Error);
    }

    [Fact]
    public void Remove_Then_Restore_TogglesIsActive()
    {
        var item = CrmSpaceItem.CreateFromItem(Guid.NewGuid(), Guid.NewGuid()).Value!;

        item.Remove();
        Assert.False(item.IsActive);

        item.Restore();
        Assert.True(item.IsActive);
    }
}

public class CrmSpaceItemAttributeValueTests
{
    [Fact]
    public void Create_TextType_WithText_Succeeds()
    {
        var result = CrmSpaceItemAttributeValue.Create(
            Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Text, valueText: "matte");

        Assert.True(result.IsSuccess);
        Assert.Equal("matte", result.Value!.ValueText);
    }

    [Fact]
    public void Create_TextType_WithWrongColumnPopulated_Fails()
    {
        var result = CrmSpaceItemAttributeValue.Create(
            Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Text, valueNumber: 5);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Create_WithNoValuePopulated_Fails()
    {
        var result = CrmSpaceItemAttributeValue.Create(Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Number);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Create_WithTwoValuesPopulated_Fails()
    {
        var result = CrmSpaceItemAttributeValue.Create(
            Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Number, valueNumber: 5, valueBool: true);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.AttributeTypeMismatch, result.Error);
    }

    [Fact]
    public void Create_SelectType_WithOptionId_Succeeds()
    {
        var optionId = Guid.NewGuid();

        var result = CrmSpaceItemAttributeValue.Create(
            Guid.NewGuid(), Guid.NewGuid(), CatalogAttributeType.Select, optionId: optionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(optionId, result.Value!.OptionId);
    }
}
