using BuildingBlocks;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Services;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Services/EffectivePermissionService.cs</c> — the security
/// core — **unchanged**, per the plan's own instruction and the review's agreement: this is not the
/// moment to improve authorization semantics, only to relocate them.
///
/// Algorithm, exactly as before: resolve the user's role names, union their role claims (role claims
/// are additive — a user in multiple roles gets the union, never a subtraction), then apply
/// per-user overrides on top — a grant adds a permission the roles don't confer, a deny removes one
/// they do. Deny-wins-over-grant is not arbitrated in this loop; it is a data invariant enforced by the
/// unique <c>(UserId, Permission)</c> index on <c>user_permission_overrides</c> (see
/// <c>UserPermissionOverrideConfiguration</c>) — a user can hold at most one override row per
/// permission, so there is never a "both a grant and a deny exist" case to break the tie on.
///
/// **Two known gaps, ported as-is, not fixed:** this does not check
/// <see cref="UserPermissionOverride.IsExpired"/>'s sibling concept, the override row's own
/// <c>IsActive</c> soft-delete flag (only <c>ExpiresAt</c> is filtered), and does not check the user's
/// own <c>IsActive</c> flag (a deactivated user's permissions still resolve here — deactivation is
/// enforced only at login/refresh, not at every permission check). Recorded, not silently changed —
/// changing security semantics mid-port is outside what a port should do.
/// </summary>
internal sealed class EffectivePermissionService(IdentityModuleDbContext db, UserManager<ApplicationUser> userManager)
    : IEffectivePermissionService
{
    public async Task<IEnumerable<string>> ResolveAsync(string userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return [];

        var roleNames = await userManager.GetRolesAsync(user);

        var rolePermissions = await db.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .Join(db.RoleClaims, r => r.Id, rc => rc.RoleId, (_, rc) => rc)
            .Where(rc => rc.ClaimType == PermissionClaim.Type)
            .Select(rc => rc.ClaimValue!)
            .Distinct()
            .ToListAsync(ct);

        var overrides = await db.UserPermissionOverrides
            .AsNoTracking()
            .Where(o => o.UserId == userId && (o.ExpiresAt == null || o.ExpiresAt > DateTime.UtcNow))
            .ToListAsync(ct);

        var effective = new HashSet<string>(rolePermissions);
        foreach (var permissionOverride in overrides)
        {
            if (permissionOverride.IsGranted)
                effective.Add(permissionOverride.Permission);
            else
                effective.Remove(permissionOverride.Permission);
        }

        return effective;
    }
}
