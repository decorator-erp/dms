using Finance.Domain;
using Xunit;

namespace Finance.UnitTests;

public class CostCategoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutACode_FailsWithCodeRequired(string code)
    {
        var result = CostCategory.Create(code, "Joinery");

        Assert.True(result.IsFailure);
        Assert.Equal(CostCategoryErrors.CodeRequired, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAName_FailsWithNameRequired(string name)
    {
        var result = CostCategory.Create("CC-1", name);

        // Specifically NameRequired: the code IS present here, so a guard that checked the wrong field
        // would surface as CodeRequired rather than passing.
        Assert.True(result.IsFailure);
        Assert.Equal(CostCategoryErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Create_StartsActive()
    {
        var category = CostCategory.Create("CC-1", "Joinery", "Carpentry work").Value!;

        Assert.True(category.IsActive);
        Assert.Equal("CC-1", category.Code);
        Assert.Equal("Joinery", category.Name);
        Assert.Equal("Carpentry work", category.Description);
    }

    [Fact]
    public void Update_RejectsAnEmptyName_AndLeavesTheCategoryUntouched()
    {
        var category = CostCategory.Create("CC-1", "Joinery").Value!;

        var result = category.Update("  ", "new description");

        Assert.True(result.IsFailure);
        Assert.Equal(CostCategoryErrors.NameRequired, result.Error);
        Assert.Equal("Joinery", category.Name);
        Assert.Null(category.Description);
    }

    /// <summary>The code is immutable after creation — <c>Update</c> takes name and description only,
    /// because the code is the stable key other records and humans refer to.</summary>
    [Fact]
    public void Update_ChangesNameAndDescription_ButNeverTheCode()
    {
        var category = CostCategory.Create("CC-1", "Joinery").Value!;

        Assert.True(category.Update("Carpentry", "Renamed").IsSuccess);

        Assert.Equal("Carpentry", category.Name);
        Assert.Equal("Renamed", category.Description);
        Assert.Equal("CC-1", category.Code);
    }

    [Fact]
    public void DeactivateAndActivate_RoundTripIsActive()
    {
        var category = CostCategory.Create("CC-1", "Joinery").Value!;

        category.Deactivate();
        Assert.False(category.IsActive);

        category.Activate();
        Assert.True(category.IsActive);
    }
}
