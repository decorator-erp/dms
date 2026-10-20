using System.Linq.Expressions;
using Hr.Contracts;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.Extensions.Logging;

namespace Hr.Infrastructure.Services;

/// <summary>
/// Aug 4.3: the two halves of an <see cref="EmployeeProfile"/>, shared by <see cref="HrDirectory"/> and
/// the employee list query so the two cannot drift: <see cref="ToSummary"/>, the single HR projection,
/// and <see cref="Zip"/>, which joins it with Identity's names in memory. Here, beside
/// <see cref="HrDirectory"/>, because Hr.Application already sees this assembly's internals
/// (<c>InternalsVisibleTo</c>), so neither caller needs a new reference.
/// </summary>
internal static class EmployeeProfiles
{
    /// <summary>
    /// <see cref="Employee"/> has no <c>PrimaryDepartmentId</c> field of its own - membership lives in
    /// <see cref="EmployeeDepartment"/>, so this resolves it as a correlated scalar subquery (translates
    /// to SQL directly; EF Core supports this pattern) rather than loading the join table separately and
    /// mapping in memory.
    ///
    /// <para>K1: an employee has at most one active membership (a partial unique index enforces it), so
    /// the subquery has exactly one candidate and needs no ordering. Before, it had to pick arbitrarily
    /// between several.</para>
    /// </summary>
    public static Expression<Func<Employee, EmployeeSummary>> ToSummary(HrDbContext db) => e =>
        new EmployeeSummary(
            e.Id, e.AppUserId, e.JobTitle,
            db.EmployeeDepartments
                .Where(ed => ed.EmployeeId == e.Id && ed.IsActive)
                .Select(ed => (Guid?)ed.DepartmentId)
                .FirstOrDefault(),
            e.IsActive);

    /// <summary>Joins HR's summaries with Identity's users, keeping the summaries' order.</summary>
    public static List<EmployeeProfile> Zip(
        IReadOnlyList<EmployeeSummary> summaries, IReadOnlyList<UserSummary> users, ILogger logger)
    {
        var usersById = users.ToDictionary(u => u.Id);

        return [.. summaries.Select(s =>
        {
            if (!usersById.TryGetValue(s.AppUserId, out UserSummary? user))
            {
                // Aug 3.9: an employee whose login is gone renders with an empty name. That is a data
                // problem someone should see, not a silent blank. Ids only, no personal data.
                logger.LogWarning(
                    "Employee {EmployeeId} points at login {AppUserId}, which Identity does not have.",
                    s.Id, s.AppUserId);
            }

            return new EmployeeProfile(
                s.Id, s.AppUserId, user?.DisplayName ?? string.Empty, user?.Email,
                s.JobTitle, s.PrimaryDepartmentId, s.IsActive);
        })];
    }
}
