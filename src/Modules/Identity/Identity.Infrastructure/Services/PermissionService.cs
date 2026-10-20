using BuildingBlocks;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Infrastructure.Services;

internal sealed class PermissionService(
    IdentityModuleDbContext db,
    RoleManager<ApplicationRole> roleManager,
    ISecurityStamps securityStamps,
    IPermissionCatalog catalog)
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

        // Aug 3.2: through the change tracker, so the removal is audited (ExecuteDeleteAsync was not), and
        // only a claim the role actually holds counts as a change. A held claim is removed even when the
        // catalog no longer knows its code: a stale claim must stay removable.
        List<ApplicationRoleClaim> held = await db.RoleClaims
            .Where(rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type && rc.ClaimValue == permission)
            .ToListAsync(ct);
        if (held.Count == 0)
        {
            // Not held: an unknown code is a bad request; a known one is already in the requested state,
            // the same idempotent 204 as assigning a permission the role already holds. No bump, nothing
            // to audit.
            return catalog.All().SelectMany(m => m.Permissions).Contains(permission)
                ? Result.Success()
                : PermissionErrors.InvalidPermission(permission);
        }

        db.RoleClaims.RemoveRange(held);
        await db.SaveChangesAsync(ct);

        // N2: every holder's tokens still claim the removed permission; refuse them.
        await securityStamps.BumpRoleHoldersAsync(roleId, ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetRolePermissionsAsync(
        string roleId, CancellationToken ct = default) =>
        await db.RoleClaims
            .AsNoTracking() // Aug 4.4: a read
            .Where(rc => rc.RoleId == roleId && rc.ClaimType == PermissionClaim.Type)
            .ToListAsync(ct);

    // Aug 3.3: Distinct() over whole rows never removed anything, because every row has its own Id and
    // RoleId, so a permission held through two roles came back twice. One row per code, picked by
    // ClaimValue; the rows are small and per-user, so the dedup runs after the read.
    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetUserPermissionsAsync(
     string userId, CancellationToken ct = default)
    {
        List<ApplicationRoleClaim> rows = await (
            from ur in db.UserRoles
            join rc in db.RoleClaims on ur.RoleId equals rc.RoleId
            where ur.UserId == userId && rc.ClaimType == PermissionClaim.Type
            select rc
            )
            .AsNoTracking()
            .ToListAsync(ct);

        return OnePerCode(rows);
    }

    public async Task<IReadOnlyList<ApplicationRoleClaim>> GetAllPermissionsAsync(CancellationToken ct = default) =>
        OnePerCode(await db.RoleClaims
            .Where(rc => rc.ClaimType == PermissionClaim.Type)
            .AsNoTracking()
            .ToListAsync(ct));

    private static List<ApplicationRoleClaim> OnePerCode(List<ApplicationRoleClaim> rows) =>
        [.. rows.OrderBy(rc => rc.ClaimValue, StringComparer.Ordinal).DistinctBy(rc => rc.ClaimValue)];
}
