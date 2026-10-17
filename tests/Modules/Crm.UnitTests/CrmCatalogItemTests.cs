using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmCatalogItemTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var categoryId = Guid.NewGuid();

        Result<CrmCatalogItem> result = CrmCatalogItem.Create(categoryId, "bed-m01", "Bed M-01", "سرير م-01");

        Assert.True(result.IsSuccess);
        Assert.Equal(categoryId, result.Value!.CategoryId);
        Assert.True(result.Value.IsPurchasable);
    }

    [Fact]
    public void Create_WithEmptyCategoryId_Fails()
    {
        Result<CrmCatalogItem> result = CrmCatalogItem.Create(Guid.Empty, "bed-m01", "Bed M-01", "سرير م-01");

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.CategoryNotFound, result.Error);
    }

    [Fact]
    public void Update_WithValidData_Succeeds()
    {
        CrmCatalogItem item = CrmCatalogItem.Create(Guid.NewGuid(), "bed-m01", "Bed M-01", "سرير م-01").Value!;

        Result result = item.Update("bed-m01", "Bed M-01 v2", "سرير م-01 v2", false, null, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bed M-01 v2", item.NameEn);
        Assert.False(item.IsPurchasable);
    }

    [Fact]
    public void MoveToCategory_WithValidId_Succeeds()
    {
        CrmCatalogItem item = CrmCatalogItem.Create(Guid.NewGuid(), "bed-m01", "Bed M-01", "سرير م-01").Value!;
        var newCategoryId = Guid.NewGuid();

        Result result = item.MoveToCategory(newCategoryId);

        Assert.True(result.IsSuccess);
        Assert.Equal(newCategoryId, item.CategoryId);
    }

    [Fact]
    public void MoveToCategory_WithEmptyId_Fails()
    {
        CrmCatalogItem item = CrmCatalogItem.Create(Guid.NewGuid(), "bed-m01", "Bed M-01", "سرير م-01").Value!;

        Result result = item.MoveToCategory(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.CategoryNotFound, result.Error);
    }
}

public class CrmCatalogAttributeTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var categoryId = Guid.NewGuid();

        Result<CrmCatalogAttribute> result = CrmCatalogAttribute.Create(categoryId, "style", "Style", "الطراز", CatalogAttributeType.Select);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogAttributeType.Select, result.Value!.DataType);
    }

    [Fact]
    public void Create_WithEmptyCategoryId_Fails()
    {
        Result<CrmCatalogAttribute> result = CrmCatalogAttribute.Create(Guid.Empty, "style", "Style", "الطراز", CatalogAttributeType.Select);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.CategoryNotFound, result.Error);
    }

    [Fact]
    public void Update_DoesNotChangeDataType()
    {
        CrmCatalogAttribute attribute = CrmCatalogAttribute.Create(
            Guid.NewGuid(), "style", "Style", "الطراز", CatalogAttributeType.Select).Value!;

        Result result = attribute.Update("style", "Style v2", "الطراز v2", "unit", true);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogAttributeType.Select, attribute.DataType);
        Assert.True(attribute.IsRequired);
    }
}

public class CrmCatalogAttributeOptionTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var attributeId = Guid.NewGuid();

        Result<CrmCatalogAttributeOption> result = CrmCatalogAttributeOption.Create(attributeId, "modern", "Modern", "حديث");

        Assert.True(result.IsSuccess);
        Assert.Equal(attributeId, result.Value!.AttributeId);
    }

    [Fact]
    public void Create_WithMissingName_Fails()
    {
        Result<CrmCatalogAttributeOption> result = CrmCatalogAttributeOption.Create(Guid.NewGuid(), "modern", "", "حديث");

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.InvalidData, result.Error);
    }
}

public class CrmCatalogCategorySpaceTypeTests
{
    [Fact]
    public void Create_WithValidIds_Succeeds()
    {
        Result<CrmCatalogCategorySpaceType> result = CrmCatalogCategorySpaceType.Create(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_WithEmptyCategoryId_Fails()
    {
        Result<CrmCatalogCategorySpaceType> result = CrmCatalogCategorySpaceType.Create(Guid.Empty, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.CategoryNotFound, result.Error);
    }

    [Fact]
    public void Create_WithEmptySpaceTypeId_Fails()
    {
        Result<CrmCatalogCategorySpaceType> result = CrmCatalogCategorySpaceType.Create(Guid.NewGuid(), Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.SpaceTypeNotFound, result.Error);
    }
}
