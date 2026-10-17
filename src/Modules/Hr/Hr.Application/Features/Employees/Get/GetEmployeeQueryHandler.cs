using BuildingBlocks.Messaging;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.Get;

internal sealed class GetEmployeeQueryHandler(HrDbContext db) : IQueryHandler<GetEmployeeQuery, EmployeeResponse>
{
    public async Task<Result<EmployeeResponse>> HandleAsync(GetEmployeeQuery query, CancellationToken ct = default)
    {
        Employee? employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == query.EmployeeId, ct);

        return employee is null ? EmployeeErrors.NotFound : ResponseMapping.ToResponse(employee);
    }
}

/// <summary>Maps in memory, not via an EF `Select` projection — `ContractType.ToString()` isn't
/// something every EF provider translates to SQL, and this isn't a hot enough path to be worth finding
/// out the hard way.</summary>
internal static class ResponseMapping
{
    public static EmployeeResponse ToResponse(Employee e) =>
        new(e.Id, e.AppUserId, e.JobTitle, e.HireDate, e.EndDate, e.Notes, e.ContractType.ToString(),
            e.PhoneNumber, e.EmergencyContactPhone, e.IsActive);
}
