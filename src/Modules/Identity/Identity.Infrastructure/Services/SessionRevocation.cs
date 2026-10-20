using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure.Services;

/// <inheritdoc cref="ISessionRevocation"/>
internal sealed class SessionRevocation(IServiceScopeFactory scopeFactory, IUserSessionCache sessionCache)
    : ISessionRevocation
{
    public async Task RevokeAllInOwnTransactionAsync(string userId, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is not null)
        {
            user.RevokeAllRefreshTokens(DateTime.UtcNow);

            // Saves the revocations along with the new stamp.
            await userManager.UpdateSecurityStampAsync(user);
        }

        sessionCache.Evict(userId);
    }
}
