using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Services;

/// <inheritdoc cref="ISecurityStamps"/>
internal sealed class SecurityStamps(
    UserManager<ApplicationUser> userManager, IdentityModuleDbContext db, IUserSessionCache sessionCache)
    : ISecurityStamps
{
    public async Task BumpAsync(string userId, CancellationToken ct = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is not null)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        sessionCache.Evict(userId);
    }

    public async Task BumpRoleHoldersAsync(string roleId, CancellationToken ct = default)
    {
        List<string> holders = await db.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        foreach (string userId in holders)
        {
            await BumpAsync(userId, ct);
        }
    }
}
