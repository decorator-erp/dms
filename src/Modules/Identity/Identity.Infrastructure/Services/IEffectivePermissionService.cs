namespace Identity.Infrastructure.Services;

internal interface IEffectivePermissionService
{
    Task<IEnumerable<string>> ResolveAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// K6: whether any active user other than <paramref name="excludingUserId"/> effectively holds
    /// <paramref name="permission"/>, so that denying it to that one user still leaves somebody able to
    /// use it.
    /// </summary>
    Task<bool> HasOtherActiveHolderAsync(string permission, string excludingUserId, CancellationToken ct = default);
}
