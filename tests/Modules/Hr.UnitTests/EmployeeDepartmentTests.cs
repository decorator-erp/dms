using Hr.Domain;
using Xunit;

namespace Hr.UnitTests;

public class EmployeeDepartmentTests
{
    [Fact]
    public void Create_SetsEmployeeAndDepartmentIds_AndIsActive()
    {
        var employeeId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var membership = EmployeeDepartment.Create(employeeId, departmentId);

        Assert.Equal(employeeId, membership.EmployeeId);
        Assert.Equal(departmentId, membership.DepartmentId);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public void Remove_SetsIsActiveFalse()
    {
        var membership = EmployeeDepartment.Create(Guid.NewGuid(), Guid.NewGuid());

        membership.Remove();

        Assert.False(membership.IsActive);
    }
}
