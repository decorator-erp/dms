using Hr.Contracts.Employees;
using Hr.Domain;

namespace Hr.Application.Features.Employees.Get;

/// <summary>Maps in memory, not via an EF `Select` projection — `ContractType.ToString()` isn't
/// something every EF provider translates to SQL, and this isn't a hot enough path to be worth finding
/// out the hard way.</summary>
internal static class ResponseMapping
{
    public static EmployeeResponse ToResponse(Employee e) =>
        new(e.Id, e.AppUserId, e.JobTitle, e.HireDate, e.EndDate, e.Notes, e.ContractType.ToString(),
            e.PhoneNumber, e.EmergencyContactPhone, e.IsActive);
}
