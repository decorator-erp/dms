using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <summary>Ported from <c>../backend/Infrastructure/Services/RoleService.cs</c>, with one bug fixed
/// on the way in (D7): <see cref="UserInRoleAsync"/> resolves the role's <em>name</em> before calling
/// <see cref="UserManager{TUser}.IsInRoleAsync"/> — the backend passed a role <em>id</em> to that
/// method, which expects a name, so the check silently always returned false there.</summary>
internal sealed class RoleService(
    RoleManager<ApplicationRole> roleManager, UserManager<ApplicationUser> userManager, IdentityModuleDbContext db)
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

        return result.Succeeded ? Result.Success() : IdentityErrorsToError("roles.AssignFailed", result);
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

        return result.Succeeded ? Result.Success() : IdentityErrorsToError("roles.RemoveFailed", result);
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

    public async Task<IReadOnlyList<ApplicationRole>> GetUserRolesAsync(string userId, CancellationToken ct = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return [];
        }

        IList<string> roleNames = await userManager.GetRolesAsync(user);
        return await db.Roles.Where(r => roleNames.Contains(r.Name!)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApplicationRole>> GetAllRolesAsync(CancellationToken ct = default) =>
        await db.Roles.ToListAsync(ct);

    private static Error IdentityErrorsToError(string code, IdentityResult result) =>
        Error.BadRequest(code, string.Join("; ", result.Errors.Select(e => e.Description)));
}
