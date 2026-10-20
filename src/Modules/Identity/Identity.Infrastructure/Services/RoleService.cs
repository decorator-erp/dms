using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <summary>Ported from <c>../backend/Infrastructure/Services/RoleService.cs</c>, with one bug fixed
/// on the way in (D7): <see cref="UserInRoleAsync"/> resolves the role's <em>name</em> before calling
/// <see cref="UserManager{TUser}.IsInRoleAsync"/> — the backend passed a role <em>id</em> to that
/// method, which expects a name, so the check silently always returned false there.</summary>
internal sealed class RoleService(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IdentityModuleDbContext db,
    ISecurityStamps securityStamps)
    : IRoleService
{
    public async Task<Result<string>> CreateRoleAsync(string roleName, CancellationToken ct = default)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return RoleErrors.DuplicatedRole(roleName);
        }

        var role = ApplicationRole.Create(Guid.NewGuid().ToString(), roleName);
        IdentityResult result = await roleManager.CreateAsync(role);

        return result.Succeeded ? role.Id : IdentityErrorsToError("roles.CreateFailed", result);
    }

    public async Task<Result> UpdateRoleAsync(string roleId, string newRoleName, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (IsSystemRole(role.Name) && !string.Equals(role.Name, newRoleName, StringComparison.Ordinal))
        {
            return RoleErrors.SystemRole(role.Name!);
        }

        role.Rename(newRoleName);
        IdentityResult result = await roleManager.UpdateAsync(role);

        return result.Succeeded ? Result.Success() : IdentityErrorsToError("roles.UpdateFailed", result);
    }

    public async Task<Result> DeleteRoleAsync(string roleId, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        // N4. The seeded roles are looked up by name (provisioning, RoleSeeder, RoleClaimSeeder,
        // AdminUserSeeder), so deleting one only makes RoleSeeder recreate it under a new id.
        if (IsSystemRole(role.Name))
        {
            return RoleErrors.SystemRole(role.Name!);
        }

        // The user_roles FK cascades: deleting a role still held would strip it from every member
        // without a trace. Members are removed first, deliberately, one assignment at a time.
        int memberCount = await db.UserRoles.CountAsync(ur => ur.RoleId == roleId, ct);
        if (memberCount > 0)
        {
            return RoleErrors.RoleInUse(roleId, memberCount);
        }

        // The role_claims FK cascades too, but a cascade happens in the database, where the audit
        // interceptor never sees it. Removing the claims through the change tracker first writes one
        // audit row per claim. Both saves share the command's transaction.
        List<ApplicationRoleClaim> claims = await db.RoleClaims.Where(rc => rc.RoleId == roleId).ToListAsync(ct);
        if (claims.Count > 0)
        {
            db.RoleClaims.RemoveRange(claims);
            await db.SaveChangesAsync(ct);
        }

        IdentityResult result = await roleManager.DeleteAsync(role);

        return result.Succeeded ? Result.Success() : IdentityErrorsToError("roles.DeleteFailed", result);
    }

    public async Task<Result> AssignRoleToUserAsync(string userId, string roleId, CancellationToken ct = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        ApplicationRole? role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        IdentityResult result = await userManager.AddToRoleAsync(user, role.Name!);
        if (!result.Succeeded)
        {
            return IdentityErrorsToError("roles.AssignFailed", result);
        }

        // N2: the user's tokens list their old roles and permissions; make them refresh.
        await securityStamps.BumpAsync(user.Id, ct);
        return Result.Success();
    }

    public async Task<Result> RemoveRoleFromUserAsync(string userId, string roleId, CancellationToken ct = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        ApplicationRole? role = await roleManager.FindByIdAsync(roleId);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        IdentityResult result = await userManager.RemoveFromRoleAsync(user, role.Name!);
        if (!result.Succeeded)
        {
            return IdentityErrorsToError("roles.RemoveFailed", result);
        }

        // N2: a removed role must stop working now, not when the token expires.
        await securityStamps.BumpAsync(user.Id, ct);
        return Result.Success();
    }

    public async Task<bool> UserInRoleAsync(string userId, string roleId, CancellationToken ct = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        ApplicationRole? role = await roleManager.FindByIdAsync(roleId);
        if (user is null || role?.Name is null)
        {
            return false;
        }

        return await userManager.IsInRoleAsync(user, role.Name);
    }

    public async Task<bool> UserIsAdminAsync(string userId, CancellationToken ct = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        IList<string> roleNames = await userManager.GetRolesAsync(user);
        return roleNames.Contains("admin", StringComparer.OrdinalIgnoreCase)
            || roleNames.Contains("manager", StringComparer.OrdinalIgnoreCase);
    }

    public Task<ApplicationRole?> GetByIdAsync(string roleId, CancellationToken ct = default) =>
        roleManager.FindByIdAsync(roleId);

    // Aug 4.4: one untracked join over the membership table. It used to load the user (with its refresh
    // tokens), then the role names, then the roles: three statements. An unknown user still answers [].
    public async Task<IReadOnlyList<ApplicationRole>> GetUserRolesAsync(string userId, CancellationToken ct = default) =>
        await db.Roles
            .AsNoTracking()
            .Where(r => db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .ToListAsync(ct);

    // Aug 4.4: a read, so untracked.
    public async Task<IReadOnlyList<ApplicationRole>> GetAllRolesAsync(CancellationToken ct = default) =>
        await db.Roles.AsNoTracking().ToListAsync(ct);

    /// <summary>N4: one of <see cref="DefaultRoles.All"/>, matched the way Identity matches role names.</summary>
    internal static bool IsSystemRole(string? roleName) =>
        roleName is not null && DefaultRoles.All.Contains(roleName, StringComparer.OrdinalIgnoreCase);

    private static Error IdentityErrorsToError(string code, IdentityResult result) =>
        Error.BadRequest(code, string.Join("; ", result.Errors.Select(e => e.Description)));
}
