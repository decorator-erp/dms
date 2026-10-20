using Identity.Contracts;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IUserAdministration"/> — declared at G1-01 for HR to call when an employee is
/// deactivated (HR → Identity is the one allowed write direction; Identity never references
/// <c>Hr.Contracts</c>). Both methods are idempotent (deactivating an already-inactive user, or
/// reactivating an already-active one, succeeds as a no-op) — required by G2-04's design: the
/// <c>OutboxProcessor</c> retries a failed <see cref="DeactivateAsync"/> call, and a retry landing after
/// an earlier attempt actually succeeded must not itself fail.
/// </summary>
internal sealed class UserAdministration(UserManager<ApplicationUser> userManager, IUserSessionCache sessionCache)
    : IUserAdministration
{
    public async Task<Result> DeactivateAsync(string userId, CancellationToken ct)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (!user.IsActive)
        {
            return Result.Success();
        }

        // N2: a deactivated user's sessions end now. Every refresh token is revoked, so they cannot refresh,
        // and the stamp is bumped, so the access tokens they hold are refused at their next request
        // instead of working until they expire. UpdateSecurityStampAsync saves all three changes at once.
        user.Deactivate();
        user.RevokeAllRefreshTokens(DateTime.UtcNow);
        IdentityResult result = await userManager.UpdateSecurityStampAsync(user);
        sessionCache.Evict(user.Id);

        return result.Succeeded
            ? Result.Success()
            : Error.BadRequest("users.DeactivateFailed", string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Result> ReactivateAsync(string userId, CancellationToken ct)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.IsActive)
        {
            return Result.Success();
        }

        user.Reactivate();
        IdentityResult result = await userManager.UpdateAsync(user);

        return result.Succeeded
            ? Result.Success()
            : Error.BadRequest("users.ReactivateFailed", string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Result> UpdateNameAsync(string userId, string firstName, string lastName, CancellationToken ct)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        Result renamed = user.UpdateName(firstName, lastName);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        // UpdateAsync, not UpdateSecurityStampAsync: the name is not in the token (B9).
        IdentityResult result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? Result.Success()
            : Error.BadRequest("users.UpdateNameFailed", string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
