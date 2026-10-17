using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Auth;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Application.Features.Auths.RevokeToken;

internal sealed class RevokeRefreshTokenCommandValidator : AbstractValidator<RevokeRefreshTokenCommand>
{
    public RevokeRefreshTokenCommandValidator()
    {
        RuleFor(x => x.Request.Token).NotEmpty();
        RuleFor(x => x.Request.RefreshToken).NotEmpty();
    }
}

/// <summary>Revokes one refresh token — as opposed to Logout, which revokes every active token on the
/// user. Same expired-access-token tolerance as refresh: the token identifies the user, it doesn't need
/// to still be live.</summary>
internal sealed class RevokeRefreshTokenCommandHandler(
    UserManager<ApplicationUser> userManager, IJwtProvider jwtProvider, IdentityModuleDbContext db)
    : ICommandHandler<RevokeRefreshTokenCommand>
{
    public async Task<Result> HandleAsync(RevokeRefreshTokenCommand command, CancellationToken ct = default)
    {
        RefreshTokenRequest request = command.Request;

        string? userId = jwtProvider.ValidateToken(request.Token, validateLifetime: false);
        if (userId is null)
        {
            return AuthErrors.InvalidToken;
        }

        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AuthErrors.InvalidUser;
        }

        Result revokeResult = user.RevokeRefreshToken(request.RefreshToken, DateTime.UtcNow);
        if (revokeResult.IsFailure)
        {
            return revokeResult;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
