using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hr.Application.Features.Departments;

/// <summary>
/// N8: builds <see cref="DepartmentResponse"/> for a page of departments, list and detail alike, with a
/// fixed number of round trips whatever the page size: one query for the heads' app-user ids, one
/// grouped count of members, and one batched <see cref="IUserDirectory.GetManyAsync"/> for the heads'
/// names. A member is an active membership of an active employee, the same rule
/// <c>IHrDirectory.DepartmentMembersAsync</c> applies.
/// </summary>
internal static class DepartmentResponses
{
    public static async Task<List<DepartmentResponse>> BuildAsync(
        HrDbContext db, IUserDirectory userDirectory, IReadOnlyList<Department> departments, CancellationToken ct)
    {
        if (departments.Count == 0)
        {
            return [];
        }

        Guid[] departmentIds = [.. departments.Select(d => d.Id)];
        Guid[] headIds = [.. departments.Where(d => d.DepartmentHeadId.HasValue).Select(d => d.DepartmentHeadId!.Value).Distinct()];

        Dictionary<Guid, string> headAppUserIds = headIds.Length == 0
            ? []
            : await db.Employees.AsNoTracking()
                .Where(e => headIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.AppUserId, ct);

        Dictionary<Guid, int> memberCounts = await db.EmployeeDepartments.AsNoTracking()
            .Where(m => departmentIds.Contains(m.DepartmentId) && m.IsActive && db.Employees.Any(e => e.Id == m.EmployeeId))
            .GroupBy(m => m.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);

        Dictionary<string, string?> names = headAppUserIds.Count == 0
            ? []
            : (await userDirectory.GetManyAsync([.. headAppUserIds.Values.Distinct()], ct))
                .ToDictionary(u => u.Id, u => u.DisplayName);

        return [.. departments.Select(d => new DepartmentResponse(
            d.Id,
            d.Name,
            d.Description,
            d.DepartmentHeadId,
            d.IsActive,
            d.DepartmentHeadId is { } headId
                && headAppUserIds.TryGetValue(headId, out string? appUserId)
                && names.TryGetValue(appUserId, out string? name)
                    ? name
                    : null,
            memberCounts.GetValueOrDefault(d.Id)))];
    }
}
