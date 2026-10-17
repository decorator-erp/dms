using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmSpaceTypeTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        Result<CrmSpaceType> result = CrmSpaceType.Create("bed_1", "Bedroom", "غرفة نوم", 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("bed_1", result.Value!.Code);
    }

    [Theory]
    [InlineData("", "en", "ar")]
    [InlineData("code", "", "ar")]
    [InlineData("code", "en", "")]
    public void Create_WithMissingRequiredField_Fails(string code, string nameEn, string nameAr)
    {
        Result<CrmSpaceType> result = CrmSpaceType.Create(code, nameEn, nameAr);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Update_ChangesNamesButNotCode()
    {
        CrmSpaceType type = CrmSpaceType.Create("bed_1", "Bedroom", "غرفة نوم").Value!;

        Result result = type.Update("Master Bedroom", "غرفة نوم رئيسية", 2);

        Assert.True(result.IsSuccess);
        Assert.Equal("bed_1", type.Code);
        Assert.Equal("Master Bedroom", type.NameEn);
    }

    [Fact]
    public void Deactivate_Then_Activate_TogglesIsActive()
    {
        CrmSpaceType type = CrmSpaceType.Create("bed_1", "Bedroom", "غرفة نوم").Value!;

        type.Deactivate();
        Assert.False(type.IsActive);

        type.Activate();
        Assert.True(type.IsActive);
    }
}

public class CrmSpaceTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        Result<CrmSpace> result = CrmSpace.Create(Guid.NewGuid(), Guid.NewGuid(), "  Kids room  ", 1, "  north-facing  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Kids room", result.Value!.Name);
        Assert.Equal("north-facing", result.Value.Description);
    }

    [Fact]
    public void Create_WithBlankName_Fails()
    {
        Result<CrmSpace> result = CrmSpace.Create(Guid.NewGuid(), Guid.NewGuid(), "   ");

        Assert.True(result.IsFailure);
        Assert.Equal(CrmSpaceErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Create_WithBlankDescription_NormalizesToNull()
    {
        Result<CrmSpace> result = CrmSpace.Create(Guid.NewGuid(), Guid.NewGuid(), "Room", description: "   ");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
    }

    [Fact]
    public void Update_OmittingDescription_ClearsIt()
    {
        CrmSpace space = CrmSpace.Create(Guid.NewGuid(), Guid.NewGuid(), "Room", description: "note").Value!;

        Result result = space.Update(space.SpaceTypeId, "Room");

        Assert.True(result.IsSuccess);
        Assert.Null(space.Description);
    }
}
