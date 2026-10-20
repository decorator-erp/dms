using Identity.Domain;

namespace Identity.Infrastructure.Services;

internal interface IEffectivePermissionService
{
    Task<IEnumerable<string>> ResolveAsync(string userId, CancellationToken ct = default);

    /// <summary>The same resolution for a user the caller has already loaded.</summary>
    Task<IEnumerable<string>> ResolveAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>
    /// K6: whether any active user other than <paramref name="excludingUserId"/> effectively holds
    /// <paramref name="permission"/>, so that denying it to that one user still leaves somebody able to
    /// use it.
    /// </summary>
    Task<bool> HasOtherActiveHolderAsync(string permission, string excludingUserId, CancellationToken ct = default);
}
