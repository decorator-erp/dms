using BuildingBlocks;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Infrastructure.Services;

internal sealed class PermissionService(
    IdentityModuleDbContext db, RoleManager<ApplicationRole> roleManager, ISecurityStamps securityStamps)
    : IPermissionService
{
    public async Task<Result> AssignPermissionToRoleAsync(
        string roleId, string permission, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        // N3. RoleClaimSeeder keeps every full-access role at the whole catalog; an edit here would
        // either be a no-op (add) or be silently undone at the next boot (remove).
        if (DelegationGuard.IsFullAccess(role.Name))
        {
            return RoleErrors.FullAccessLocked(role.Name!);
        }

        bool alreadyAssigned = await db.RoleClaims.AnyAsync(
            rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type && rc.ClaimValue == permission, ct);
        if (alreadyAssigned)
        {
            return Result.Success();
        }

        db.RoleClaims.Add(ApplicationRoleClaim.Create(roleId, PermissionClaim.Type, permission));
        await db.SaveChangesAsync(ct);

        // N2: every holder's tokens now under-state what the role confers.
        await securityStamps.BumpRoleHoldersAsync(roleId, ct);
        return Result.Success();
    }

    public async Task<Result> RemovePermissionFromRoleAsync(
        string roleId, string permission, CancellationToken ct = default)
    {
        // The role is loaded for N3's check; a missing one is reported rather than answered with a 204.
        ApplicationRole? role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (DelegationGuard.IsFullAccess(role.Name))
        {
            return RoleErrors.FullAccessLocked(role.Name!);
        }

        await db.RoleClaims
            .Where(rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type && rc.ClaimValue == permission)
            .ExecuteDeleteAsync(ct);

        // N2: every holder's tokens still claim the removed permission; refuse them.
        await securityStamps.BumpRoleHoldersAsync(roleId, ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetRolePermissionsAsync(
        string roleId, CancellationToken ct = default) =>
        await db.RoleClaims
            .Where(rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetUserPermissionsAsync(
     string userId, CancellationToken ct = default)
    {

        List<ApplicationRoleClaim> q = await (
            from ur in db.UserRoles
            join rc in db.RoleClaims on ur.RoleId equals rc.RoleId
            where ur.UserId == userId && rc.ClaimType == PermissionClaim.Type
            select rc
            )
            .Distinct()
            .ToListAsync(ct);

        return q;
    }

    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetAllPermissionsAsync(CancellationToken ct = default) =>
        await db.RoleClaims
            .Where(rc => rc.ClaimType == PermissionClaim.Type)
            .Distinct()
            .ToListAsync(ct);
}
