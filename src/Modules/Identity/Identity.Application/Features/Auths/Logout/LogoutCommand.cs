using BuildingBlocks;
using BuildingBlocks.Messaging;
using Identity.Contracts.Auth;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Application.Features.Auths.Logout;

/// <summary>
/// K5: <c>POST /api/auth/logout</c> revokes every refresh token of the caller, as opposed to
/// <c>/revoke</c>, which revokes one. It also bumps the security stamp (N2), so the access tokens
/// still in flight are refused at their next request rather than living out their hour: "log out
/// everywhere" means everywhere now.
/// </summary>
internal sealed class LogoutCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUser currentUser,
    ISecurityStamps securityStamps,
    IdentityModuleDbContext db)
    : ICommandHandler<LogoutCommand>
{
    public async Task<Result> HandleAsync(LogoutCommand command, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
        {
            return AuthErrors.UnAuthorization;
        }

        ApplicationUser? user = await userManager.FindByIdAsync(currentUser.UserId);
        if (user is null)
        {
            return AuthErrors.InvalidUser;
        }

        user.RevokeAllRefreshTokens(DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await securityStamps.BumpAsync(user.Id, ct);

        return Result.Success();
    }
}
