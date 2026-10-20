using BuildingBlocks;
using BuildingBlocks.Messaging;
using Hr.Application.Features.Employees.Get;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.GetDetail;

/// <summary>
/// Aug 3.10: the employee DETAIL read, moved out of the endpoint into a query. It used to be composed
/// in <c>EmployeeEndpoints</c> (bypassing the pipeline). One HR read, then one batched
/// <see cref="IHrDirectory.GetProfilesAsync"/> for the name, email and department that
/// <see cref="EmployeeResponse"/> does not carry, and the login's state for <c>LoginActive</c> (N7).
/// </summary>
internal static class EmployeeDetail
{
    public static async Task<Result<EmployeeDetailResponse>> BuildAsync(
        HrDbContext db, IHrDirectory hrDirectory, IUserDirectory userDirectory, Guid employeeId, CancellationToken ct)
    {
        // IgnoreQueryFilters: the detail page shows a deactivated employee too, or there would be no page
        // to reactivate them from.
        Employee? row = await db.Employees.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (row is null)
        {
            return EmployeeErrors.NotFound;
        }

        EmployeeResponse employee = ResponseMapping.ToResponse(row);
        IReadOnlyList<EmployeeProfile> profiles = await hrDirectory.GetProfilesAsync([employeeId], ct);
        EmployeeProfile? profile = profiles.Count > 0 ? profiles[0] : null;
        UserSummary? login = await userDirectory.GetAsync(employee.AppUserId, ct);

        return new EmployeeDetailResponse(
            employee.Id, employee.AppUserId, employee.JobTitle, employee.HireDate, employee.EndDate,
            employee.Notes, employee.ContractType, employee.PhoneNumber, employee.EmergencyContactPhone,
            employee.IsActive, profile?.FullName ?? string.Empty, profile?.Email,
            profile?.PrimaryDepartmentId, login?.IsActive ?? false);
    }
}

internal sealed class GetEmployeeDetailQueryHandler(
    HrDbContext db, IHrDirectory hrDirectory, IUserDirectory userDirectory)
    : IQueryHandler<GetEmployeeDetailQuery, EmployeeDetailResponse>
{
    public Task<Result<EmployeeDetailResponse>> HandleAsync(GetEmployeeDetailQuery query, CancellationToken ct = default) =>
        EmployeeDetail.BuildAsync(db, hrDirectory, userDirectory, query.EmployeeId, ct);
}

/// <summary>
/// <c>GET /api/employees/me</c>: the caller's own record, through the same composition as the detail
/// route so the two cannot answer the same question differently. A caller with no (active) employee
/// record, any client user for one, gets the ProblemDetails 404 every other HR route gives, not the bare
/// 404 the endpoint used to return.
/// </summary>
internal sealed class GetMyEmployeeDetailQueryHandler(
    HrDbContext db, IHrDirectory hrDirectory, IUserDirectory userDirectory, ICurrentUser currentUser)
    : IQueryHandler<GetMyEmployeeDetailQuery, EmployeeDetailResponse>
{
    public async Task<Result<EmployeeDetailResponse>> HandleAsync(GetMyEmployeeDetailQuery query, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { Length: > 0 } appUserId)
        {
            return Error.Unauthorized("employees.Unauthenticated", "No signed-in caller.");
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(appUserId, ct);
        return employeeId is null
            ? EmployeeErrors.NotFound
            : await EmployeeDetail.BuildAsync(db, hrDirectory, userDirectory, employeeId.Value, ct);
    }
}
