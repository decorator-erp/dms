using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmCatalogCategoryTests
{
    [Fact]
    public void Create_Root_WithValidData_Succeeds()
    {
        Result<CrmCatalogCategory> result = CrmCatalogCategory.Create(null, "beds", "Beds", "أسرة");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ParentId);
        Assert.Equal(string.Empty, result.Value.Path);
        Assert.Equal(0, result.Value.Depth);
    }

    [Fact]
    public void Create_Child_WithValidData_Succeeds()
    {
        var parentId = Guid.NewGuid();

        Result<CrmCatalogCategory> result = CrmCatalogCategory.Create(parentId, "queen_beds", "Queen Beds", "أسرة كوين");

        Assert.True(result.IsSuccess);
        Assert.Equal(parentId, result.Value!.ParentId);
    }

    [Theory]
    [InlineData("", "en", "ar")]
    [InlineData("code", "", "ar")]
    [InlineData("code", "en", "")]
    public void Create_WithMissingRequiredField_Fails(string code, string nameEn, string nameAr)
    {
        Result<CrmCatalogCategory> result = CrmCatalogCategory.Create(null, code, nameEn, nameAr);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmCatalogErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Update_WithValidData_Succeeds()
    {
        CrmCatalogCategory category = CrmCatalogCategory.Create(null, "beds", "Beds", "أسرة").Value!;

        Result result = category.Update("beds", "Bedroom Beds", "أسرة غرفة النوم", CatalogSelectionMode.Single, true, null, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bedroom Beds", category.NameEn);
        Assert.Equal(CatalogSelectionMode.Single, category.SelectionMode);
        Assert.True(category.IsRequired);
    }

    [Fact]
    public void SetPath_UpdatesPathAndDepth()
    {
        CrmCatalogCategory category = CrmCatalogCategory.Create(null, "beds", "Beds", "أسرة").Value!;

        category.SetPath($"/{category.Id}/", 0);

        Assert.Equal($"/{category.Id}/", category.Path);
        Assert.Equal(0, category.Depth);
    }

    [Fact]
    public void SetParent_ChangesParentId()
    {
        CrmCatalogCategory category = CrmCatalogCategory.Create(null, "beds", "Beds", "أسرة").Value!;
        var newParentId = Guid.NewGuid();

        category.SetParent(newParentId);

        Assert.Equal(newParentId, category.ParentId);
    }

    [Fact]
    public void Deactivate_Then_Activate_TogglesIsActive()
    {
        CrmCatalogCategory category = CrmCatalogCategory.Create(null, "beds", "Beds", "أسرة").Value!;

        category.Deactivate();
        Assert.False(category.IsActive);

        category.Activate();
        Assert.True(category.IsActive);
    }
}
