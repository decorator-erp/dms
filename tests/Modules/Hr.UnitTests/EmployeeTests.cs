using Hr.Domain;
using Xunit;

namespace Hr.UnitTests;

public class EmployeeTests
{
    [Fact]
    public void Create_SetsAllProvidedFields()
    {
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var employee = Employee.Create(
            appUserId: "user-1", jobTitle: "Engineer", hireDate: hireDate, contractType: ContractType.FullTime,
            phoneNumber: "555-0100", emergencyContactName: "Jane", emergencyContactPhone: "555-0200", notes: "n");

        Assert.Equal("user-1", employee.AppUserId);
        Assert.Equal("Engineer", employee.JobTitle);
        Assert.Equal(hireDate, employee.HireDate);
        Assert.Equal(ContractType.FullTime, employee.ContractType);
        Assert.True(employee.IsActive);
    }

    [Fact]
    public void Update_ChangesOnlyProvidedFields()
    {
        var employee = Employee.Create("user-1", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime);

        employee.Update(jobTitle: "Senior Engineer");

        Assert.Equal("Senior Engineer", employee.JobTitle);
        Assert.Equal(ContractType.FullTime, employee.ContractType);
    }

    [Fact]
    public void Update_OnInactiveEmployee_IsNoOp()
    {
        var employee = Employee.Create("user-1", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime);
        employee.Deactivate();

        employee.Update(jobTitle: "Senior Engineer");

        Assert.Equal("Engineer", employee.JobTitle);
    }

    [Fact]
    public void Deactivate_ThenActivate_RestoresIsActive()
    {
        var employee = Employee.Create("user-1", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime);

        employee.Deactivate();
        Assert.False(employee.IsActive);

        employee.Activate();
        Assert.True(employee.IsActive);
    }
}
