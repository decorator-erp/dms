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
///
/// <para>Since then: expiry is <see cref="UserPermissionOverride.LiveAt"/>, the predicate every override
/// read shares (Aug 2.4); the override's own <c>IsActive</c> flag is still not checked (nothing sets it
/// false yet, Phase 5); and a deactivated user's tokens are refused at every request by the security
/// stamp check (Phase 1B, N2), so the second gap no longer reaches an API caller.</para>
///
/// <para><b>Phase 5.</b> Gap 1 is closed: <see cref="UserPermissionOverride.LiveAt"/> now also requires
/// the override's own <c>IsActive</c>, here, in the last-holder check, in the grant/deny existence check
/// and in the <c>/effective</c> listing. Nothing sets it false yet (revoke is a hard delete); the check is
/// for a future soft revoke. Gap 2 stays as the 1B note above says: covered by the security stamp at the
/// API. And deny-wins is now explicit rather than resting on the unique index: the in-memory step is
/// <see cref="PermissionResolution.Resolve"/>, which adds every live grant and then removes every live
/// deny.</para>
///
/// <para><b>Aug 4.2: one query, same algorithm.</b> It used to load the user entity (tracked, with its
/// refresh tokens), then the role names, then the role claims, then the overrides: four round trips.
/// Now the role claims (through <c>user_roles</c>, the same membership <c>GetRolesAsync</c> read) and the
/// live overrides come back in one <c>UNION ALL</c>, untracked, and the union-then-overrides step runs
/// in memory exactly as before. A missing user still resolves to nothing: no memberships, no overrides.
/// The two gaps above are unchanged on purpose (Phase 5).</para>
/// </summary>
internal sealed class EffectivePermissionService(IdentityModuleDbContext db) : IEffectivePermissionService
{
    private const int FromRole = 0;
    private const int Grant = 1;
    private const int Deny = 2;

    public async Task<IEnumerable<string>> ResolveAsync(string userId, CancellationToken ct = default)
    {
        DateTime utcNow = DateTime.UtcNow;
        var rows = await (
                from ur in db.UserRoles
                where ur.UserId == userId
                join rc in db.RoleClaims on ur.RoleId equals rc.RoleId
                where rc.ClaimType == PermissionClaim.Type
                select new { Code = rc.ClaimValue!, Kind = FromRole, IsActive = true, ExpiresAt = (DateTime?)null })
            .Concat(db.UserPermissionOverrides
                .Where(o => o.UserId == userId)
                .Where(UserPermissionOverride.LiveAt(utcNow))
                .Select(o => new { Code = o.Permission, Kind = o.IsGranted ? Grant : Deny, o.IsActive, o.ExpiresAt }))
            .AsNoTracking()
            .ToListAsync(ct);

        return PermissionResolution.Resolve(
            rows.Where(r => r.Kind == FromRole).Select(r => r.Code),
            rows.Where(r => r.Kind != FromRole)
                .Select(r => new OverrideState(r.Code, r.Kind == Grant, r.IsActive, r.ExpiresAt)),
            utcNow);
    }

    /// <summary>Aug 4.2: for a caller that already holds the user (login, refresh), so nothing loads it
    /// again. The resolution itself needs only the id.</summary>
    public Task<IEnumerable<string>> ResolveAsync(ApplicationUser user, CancellationToken ct = default) =>
        ResolveAsync(user.Id, ct);

    /// <summary>
    /// Whether an active user other than <paramref name="excludingUserId"/> effectively holds
    /// <paramref name="permission"/>, weighed exactly as <see cref="ResolveAsync(string, CancellationToken)"/>
    /// weighs it: a role grants it and no live deny removes it, or a live grant adds it. An expired
    /// override counts for nothing, either way.
    ///
    /// <para>Aug 4.2: one <c>EXISTS</c> query. It used to collect the candidates, then load and resolve
    /// each one in turn (an N+1, each step loading the candidate's refresh tokens).</para>
    /// </summary>
    public async Task<bool> HasOtherActiveHolderAsync(
        string permission, string excludingUserId, CancellationToken ct = default)
    {
        IQueryable<UserPermissionOverride> live = db.UserPermissionOverrides
            .Where(UserPermissionOverride.LiveAt(DateTime.UtcNow))
            .Where(o => o.Permission == permission);

        return await db.Users
            .Where(u => u.Id != excludingUserId && u.IsActive)
            .AnyAsync(u =>
                // (via a role AND no live deny) OR a live grant; && binds tighter than ||.
                db.UserRoles.Any(ur => ur.UserId == u.Id
                        && db.RoleClaims.Any(rc => rc.RoleId == ur.RoleId
                            && rc.ClaimType == PermissionClaim.Type && rc.ClaimValue == permission))
                    && !live.Any(o => o.UserId == u.Id && !o.IsGranted)
                || live.Any(o => o.UserId == u.Id && o.IsGranted),
                ct);
    }
}
