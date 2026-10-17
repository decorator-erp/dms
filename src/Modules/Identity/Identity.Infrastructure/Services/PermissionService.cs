using BuildingBlocks;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Infrastructure.Services;

internal sealed class PermissionService(IdentityModuleDbContext db, RoleManager<ApplicationRole> roleManager)
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

        bool alreadyAssigned = await db.RoleClaims.AnyAsync(
            rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type && rc.ClaimValue == permission, ct);
        if (alreadyAssigned)
        {
            return Result.Success();
        }

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
