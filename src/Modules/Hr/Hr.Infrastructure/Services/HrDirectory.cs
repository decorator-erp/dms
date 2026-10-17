using Hr.Contracts;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hr.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IHrDirectory"/> over <see cref="HrDbContext"/> — always via a <c>Select</c>
/// projection straight to <see cref="EmployeeSummary"/>, never load-then-map, so a caller asking for one
/// field doesn't pull the whole row. Registered scoped (once per request in the real host), which is
/// what makes <see cref="_resolveCache"/> a genuine per-request memo rather than a cross-request leak.
/// </summary>
internal sealed class HrDirectory(HrDbContext db, IUserDirectory userDirectory) : IHrDirectory
{
    private readonly Dictionary<string, Guid?> _resolveCache = [];

    /// <summary>
    /// The hottest path in the system once Projects exists (per the plan's own framing) — every
    /// authorization check that needs "which employee is this caller" hits this. Memoized per DI scope:
    /// three calls with the same <paramref name="appUserId"/> within one request/scope hit the database
    /// once, not three times.
    /// </summary>
    public async Task<Guid?> ResolveEmployeeIdAsync(string appUserId, CancellationToken ct)
    {
        if (_resolveCache.TryGetValue(appUserId, out Guid? cached))
        {
            return cached;
        }

        Guid? employeeId = await db.Employees
            .Where(e => e.AppUserId == appUserId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        _resolveCache[appUserId] = employeeId;
        return employeeId;
    }

    public async Task<EmployeeSummary?> GetAsync(Guid employeeId, CancellationToken ct) =>
        await db.Employees
            .Where(e => e.Id == employeeId)
            .Select(ToSummary())
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Employees
            .Where(e => ids.Contains(e.Id))
            .Select(ToSummary())
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> DepartmentMembersAsync(Guid departmentId, CancellationToken ct) =>
        await db.EmployeeDepartments
            .Where(ed => ed.DepartmentId == departmentId && ed.IsActive)
            .Select(ed => ed.EmployeeId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DepartmentHeadSummary>> GetDepartmentHeadsAsync(
        IReadOnlyCollection<Guid> departmentIds, CancellationToken ct)
    {
        if (departmentIds.Count == 0)
        {
            return [];
        }

        return await db.Departments
            .Where(d => departmentIds.Contains(d.Id))
            .Select(d => new DepartmentHeadSummary(d.Id, d.DepartmentHeadId))
            .ToListAsync(ct);
    }

    /// <summary>Exactly two calls total, regardless of how many ids are passed: one projection here,
    /// one batched <see cref="IUserDirectory.GetManyAsync"/> — zipped in memory. Never looped per id.</summary>
    public async Task<IReadOnlyList<EmployeeProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        List<EmployeeSummary> summaries = await db.Employees
            .Where(e => ids.Contains(e.Id))
            .Select(ToSummary())
            .ToListAsync(ct);

        string[] appUserIds = summaries.Select(s => s.AppUserId).ToArray();
        IReadOnlyList<UserSummary> users = await userDirectory.GetManyAsync(appUserIds, ct);
        var usersById = users.ToDictionary(u => u.Id);

        return [.. summaries.Select(s =>
        {
            usersById.TryGetValue(s.AppUserId, out UserSummary? user);
            return new EmployeeProfile(
                s.Id, s.AppUserId, user?.DisplayName ?? string.Empty, user?.Email,
                s.JobTitle, s.PrimaryDepartmentId, s.IsActive);
        })];
    }

    /// <summary>
    /// <see cref="Domain.Employee"/> has no <c>PrimaryDepartmentId</c> field of its own - membership
    /// lives in <see cref="Domain.EmployeeDepartment"/>, so this resolves it as a correlated scalar
    /// subquery (translates to SQL directly; EF Core supports this pattern) rather than loading the
    /// join table separately and mapping in memory.
    /// </summary>
    private System.Linq.Expressions.Expression<Func<Domain.Employee, EmployeeSummary>> ToSummary() => e =>
        new EmployeeSummary(
            e.Id, e.AppUserId, e.JobTitle,
            db.EmployeeDepartments
                .Where(ed => ed.EmployeeId == e.Id && ed.IsActive)
                .Select(ed => (Guid?)ed.DepartmentId)
                .FirstOrDefault(),
            e.IsActive);
}
