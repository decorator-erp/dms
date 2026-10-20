using Hr.Domain;
using Hr.Domain.Errors;
using SharedKernel;
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
            phoneNumber: "555-0100", emergencyContactPhone: "555-0200", notes: "n");

        Assert.Equal("user-1", employee.AppUserId);
        Assert.Equal("Engineer", employee.JobTitle);
        Assert.Equal(hireDate, employee.HireDate);
        Assert.Equal(ContractType.FullTime, employee.ContractType);
        Assert.True(employee.IsActive);
    }

    /// <summary>N7: Update is a full replacement. It used to be a partial patch ("only provided fields"),
    /// under which a field could never be cleared: a null meant "leave it".</summary>
    [Fact]
    public void Update_ReplacesEveryEditableField_AndANullClears()
    {
        var employee = Employee.Create(
            "user-1", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime,
            phoneNumber: "+1 555 0100", emergencyContactPhone: "+1 555 0199", notes: "note");

        Result result = employee.Update("Senior Engineer", ContractType.Contract, null, null, null, new DateOnly(2030, 1, 1));

        Assert.True(result.IsSuccess);
        Assert.Equal("Senior Engineer", employee.JobTitle);
        Assert.Equal(ContractType.Contract, employee.ContractType);
        Assert.Null(employee.PhoneNumber);
        Assert.Null(employee.EmergencyContactPhone);
        Assert.Null(employee.Notes);
        Assert.Equal(new DateOnly(2030, 1, 1), employee.EndDate);
    }

    /// <summary>N7: an inactive employee is not edited, and says so. This used to be a silent no-op the
    /// endpoint answered with 204.</summary>
    [Fact]
    public void Update_OnInactiveEmployee_IsRefusedWithInactive()
    {
        var employee = Employee.Create("user-1", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime);
        employee.Deactivate();

        Result result = employee.Update("Senior Engineer", ContractType.FullTime, null, null, null, null);

        Assert.Equal(EmployeeErrors.Inactive, result.Error);
        Assert.Equal("Engineer", employee.JobTitle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(-1)]
    public void An_undefined_contract_type_is_refused_by_the_domain(int value)
    {
        var employee = Employee.Create("user-1", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Employee.Create("user-2", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), (ContractType)value));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            employee.Update("Engineer", (ContractType)value, null, null, null, null));
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
