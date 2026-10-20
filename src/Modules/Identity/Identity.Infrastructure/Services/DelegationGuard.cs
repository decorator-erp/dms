using BuildingBlocks;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <inheritdoc cref="IDelegationGuard"/>
internal sealed class DelegationGuard(
    ICurrentUser currentUser,
    IEffectivePermissionService effectivePermissions,
    UserManager<ApplicationUser> userManager,
    IdentityModuleDbContext db)
    : IDelegationGuard
{
    public async Task<Result> EnsureCallerHoldsAsync(IEnumerable<string> permissions, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
        {
            return AuthErrors.UnAuthorization;
        }

        HashSet<string> held = [.. await effectivePermissions.ResolveAsync(currentUser.UserId, ct)];
        string[] missing = [.. permissions.Where(p => !held.Contains(p)).Distinct().Order(StringComparer.Ordinal)];

        return missing.Length == 0 ? Result.Success() : PermissionErrors.CannotDelegate(missing);
    }

    public async Task<Result> EnsureCanAssignRoleAsync(ApplicationRole role, CancellationToken ct = default)
    {
        Result manage = await EnsureCanManageRoleAsync(role, ct);
        if (manage.IsFailure)
        {
            return manage;
        }

        List<string> conferred = await db.RoleClaims
            .AsNoTracking()
            .Where(rc => rc.RoleId == role.Id && rc.ClaimType == PermissionClaim.Type)
            .Select(rc => rc.ClaimValue!)
            .ToListAsync(ct);

        return await EnsureCallerHoldsAsync(conferred, ct);
    }

    public async Task<Result> EnsureCanManageRoleAsync(ApplicationRole role, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
        {
            return AuthErrors.UnAuthorization;
        }

        if (!IsFullAccess(role.Name))
        {
            return Result.Success();
        }

        // Membership, not permissions: a full-access role means "every permission, including the ones a
        // module registers next year". Holding today's catalog via a custom role is not the same thing.
        ApplicationUser? caller = await userManager.FindByIdAsync(currentUser.UserId);
        if (caller is null || !caller.IsActive)
        {
            return PermissionErrors.CannotDelegateFullAccessRole(role.Name!);
        }

        IList<string> callerRoles = await userManager.GetRolesAsync(caller);
        return callerRoles.Any(IsFullAccess)
            ? Result.Success()
            : PermissionErrors.CannotDelegateFullAccessRole(role.Name!);
    }

    internal static bool IsFullAccess(string? roleName) =>
        roleName is not null && DefaultRoles.FullAccessRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
}
