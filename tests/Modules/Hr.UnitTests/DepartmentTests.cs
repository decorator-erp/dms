using Hr.Domain;
using Xunit;

namespace Hr.UnitTests;

public class DepartmentTests
{
    [Fact]
    public void Create_SetsNameAndDescription()
    {
        var department = Department.Create("Engineering", "Builds the product");

        Assert.Equal("Engineering", department.Name);
        Assert.Equal("Builds the product", department.Description);
        Assert.Null(department.DepartmentHeadId);
        Assert.True(department.IsActive);
    }

    [Fact]
    public void Update_ReplacesNameAndDescription()
    {
        var department = Department.Create("Engineering", "Builds the product");

        department.Update("Engineering Org", null);

        Assert.Equal("Engineering Org", department.Name);
        Assert.Null(department.Description);
    }

    [Fact]
    public void AssignHead_ThenRemoveHead_ClearsIt()
    {
        var department = Department.Create("Engineering");
        var headId = Guid.NewGuid();

        department.AssignHead(headId);
        Assert.Equal(headId, department.DepartmentHeadId);

        department.RemoveHead();
        Assert.Null(department.DepartmentHeadId);
    }

    [Fact]
    public void Deactivate_ClearsIsActive()
    {
        var department = Department.Create("Engineering");

        department.Deactivate();
        Assert.False(department.IsActive);
    }
}
