using Hr.Domain;
using Identity.Contracts;
using Identity.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace Hr.Infrastructure.Persistence.Seeders;

/// <summary>
/// Gives Identity's seeded admin (<see cref="SeedAccounts.AdminUserId"/>) its employee profile, so that
/// every login is an employee or a client with no exception. Runs from <c>HrModule.SeedAsync</c>, after
/// Identity has seeded the user (module order in <c>Program</c>), and writes only HR's own table; the
/// user is read through <see cref="IUserDirectory"/>.
///
/// <para>Idempotent: a profile that already exists, active or not, is left alone. It also repairs a
/// database seeded before this rule existed, on the next start.</para>
/// </summary>
internal static class AdminEmployeeSeeder
{
    internal const string JobTitle = "System Administrator";

    public static async Task SeedAsync(HrDbContext db, IUserDirectory users, CancellationToken ct = default)
    {
        if (await users.GetAsync(SeedAccounts.AdminUserId, ct) is null)
        {
            // No admin seeded (Development without DMS_SEED_ADMIN_PASSWORD): nothing to link.
            return;
        }

        if (await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.AppUserId == SeedAccounts.AdminUserId, ct))
        {
            return;
        }

        db.Employees.Add(Employee.Create(
            SeedAccounts.AdminUserId, JobTitle, DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime));
        await db.SaveChangesAsync(ct);
    }
}
