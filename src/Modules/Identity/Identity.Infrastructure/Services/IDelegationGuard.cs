using Identity.Domain;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <summary>
/// N1: nobody hands out authority they don't have. Every path that widens what a user can do (assigning
/// a role, adding a permission to a role, granting an override) asks this first, so a holder of
/// <c>roles.manage</c> alone cannot make themselves (or anyone) an admin.
///
/// <para>The caller is <see cref="BuildingBlocks.ICurrentUser"/>, and "holds" means <b>effectively</b>
/// holds: resolved from the database through <see cref="IEffectivePermissionService"/>, not read from
/// the token's claims. A token minted before a role was taken away must not still be able to delegate
/// it.</para>
///
/// <para>Failures are 403 <c>permissions.CannotDelegate</c>, naming what was missing. It runs after
/// the route's own permission check and before any state check, the order CLAUDE.md requires.</para>
/// </summary>
internal interface IDelegationGuard
{
    /// <summary>The caller must effectively hold every one of <paramref name="permissions"/>.</summary>
    Task<Result> EnsureCallerHoldsAsync(IEnumerable<string> permissions, CancellationToken ct = default);

    /// <summary>
    /// Assigning <paramref name="role"/> to someone confers all of its claims, so the caller must hold
    /// them all. A full-access role additionally requires a full-access caller (see
    /// <see cref="EnsureCanManageRoleAsync"/>).
    /// </summary>
    Task<Result> EnsureCanAssignRoleAsync(ApplicationRole role, CancellationToken ct = default);

    /// <summary>
    /// Removing a role from someone, or editing a role's claims: only a full-access caller may touch a
    /// full-access role. Any other role passes.
    /// </summary>
    Task<Result> EnsureCanManageRoleAsync(ApplicationRole role, CancellationToken ct = default);
}
