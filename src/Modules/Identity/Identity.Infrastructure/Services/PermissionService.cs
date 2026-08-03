using BuildingBlocks;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <summary>Ported from <c>../backend/Infrastructure/Services/PermissionService.cs</c>. Role-derived
/// only — this does not apply per-user overrides (that is <see cref="IEffectivePermissionService"/>'s
/// job); this service is for the roles &amp; permissions ADMIN screens (G1-06), not for authorization
/// decisions.</summary>
internal sealed class PermissionService(IdentityModuleDbContext db, RoleManager<ApplicationRole> roleManager)
    : IPermissionService
{
    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetUserPermissionsAsync(
        string userId, CancellationToken ct = default)
    {
        var roleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        return await db.RoleClaims
            .Where(rc => roleIds.Contains(rc.RoleId) && rc.ClaimType == PermissionClaim.Type)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetAllPermissionsAsync(CancellationToken ct = default) =>
        await db.RoleClaims
            .Where(rc => rc.ClaimType == PermissionClaim.Type)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>Idempotent — assigning an already-assigned permission is a no-op success, not an error.</summary>
    public async Task<Result> AssignPermissionToRoleAsync(
        string roleId, string permission, CancellationToken ct = default)
    {
        var role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
            return RoleErrors.NotFound;

        var alreadyAssigned = await db.RoleClaims.AnyAsync(
            rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type && rc.ClaimValue == permission, ct);
        if (alreadyAssigned)
            return Result.Success();

        db.RoleClaims.Add(ApplicationRoleClaim.Create(roleId, PermissionClaim.Type, permission));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemovePermissionFromRoleAsync(
        string roleId, string permission, CancellationToken ct = default)
    {
        await db.RoleClaims
            .Where(rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type && rc.ClaimValue == permission)
            .ExecuteDeleteAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetRolePermissionsAsync(
        string roleId, CancellationToken ct = default) =>
        await db.RoleClaims
            .Where(rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type)
            .ToListAsync(ct);
}
