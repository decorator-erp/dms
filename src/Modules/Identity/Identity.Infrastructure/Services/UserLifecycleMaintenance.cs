using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IUserLifecycleMaintenance"/>. Every guard here is a refusal rather than a
/// filter: the caller decides which candidates to delete, but it cannot talk this class into deleting a
/// live account or the seeded admin.
/// </summary>
internal sealed class UserLifecycleMaintenance(
    UserManager<ApplicationUser> userManager,
    IdentityModuleDbContext db,
    ILogger<UserLifecycleMaintenance> logger) : IUserLifecycleMaintenance
{
    public async Task<IReadOnlyList<string>> ListUnactivatedAsync(
        DateTime createdBeforeUtc, int limit, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => !u.IsActive && u.CreatedAt < createdBeforeUtc)
            .OrderBy(u => u.CreatedAt)
            .Take(limit)
            .Select(u => u.Id)
            .ToListAsync(ct);

    public async Task<Result> DeleteUnactivatedAsync(string userId, CancellationToken ct)
    {
        // Belt and braces. The seeded admin is active, so the IsActive guard below already covers it —
        // but this account is the one that cannot be recreated by any other path in the system, and it
        // should not depend on a second rule to survive.
        if (userId == AdminUserSeeder.SeedAdminUserId)
        {
            return Error.Conflict("users.CannotDeleteSeedAdmin", "The seeded administrator cannot be deleted.");
        }

        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            // Idempotent, matching UserAdministration: a retry after a successful delete is a no-op,
            // not a failure.
            return Result.Success();
        }

        if (user.IsActive)
        {
            return Error.Conflict(
                "users.CannotDeleteActiveUser",
                "An active user cannot be deleted through lifecycle maintenance.");
        }

        IdentityResult result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Failed to delete unactivated user {UserId}: {IdentityErrors}",
                userId,
                string.Join("; ", result.Errors.Select(e => e.Description)));

            return Error.BadRequest("users.DeleteFailed", "The user could not be deleted.");
        }

        return Result.Success();
    }
}
