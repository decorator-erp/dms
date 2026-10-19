using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Auth;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Identity.Application.Features.Auths.RefreshToken;

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.Request.Token).NotEmpty();
        RuleFor(x => x.Request.RefreshToken).NotEmpty();
    }
}

/// <summary>
/// Ported from <c>AuthService.GetRefreshTokenAsync</c> — rotation. The presented access token is
/// validated with <c>validateLifetime: false</c> deliberately: it identifies the user, but is expected
/// to already be expired (that is the whole reason a caller is refreshing). Reuse of an
/// already-rotated or revoked refresh token is rejected by
/// <see cref="ApplicationUser.RotateRefreshToken"/> itself (G1-02), not re-checked here.
///
/// <para>K5: a replayed (already-rotated) token means the family is compromised.
/// <see cref="ISessionRevocation"/> revokes every refresh token and bumps the stamp <b>in its own
/// transaction</b>, because this command then fails and its own transaction rolls back.</para>
///
/// <para>The access token's security stamp is deliberately <i>not</i> checked here. A bumped stamp
/// (new roles, a permission change) is exactly what refresh exists to pick up; the refresh token is the
/// credential, and the events that must end a session revoke it.</para>
/// </summary>
internal sealed class RefreshTokenCommandHandler(
    UserManager<ApplicationUser> userManager,
    IJwtProvider jwtProvider,
    ISessionRevocation sessionRevocation,
    IdentityModuleDbContext db,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> HandleAsync(RefreshTokenCommand command, CancellationToken ct = default)
    {
        RefreshTokenRequest request = command.Request;

        string? userId = jwtProvider.ValidateToken(request.Token, validateLifetime: false);
        if (userId is null)
        {
            return AuthErrors.InvalidToken;
        }

        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            return AuthErrors.InvalidUser;
        }

        DateTime utcNow = DateTime.UtcNow;
        string newRefreshToken = RefreshTokenValues.New();
        Result<Domain.RefreshToken> rotateResult = user.RotateRefreshToken(
            request.RefreshToken,
            newRefreshToken,
            utcNow,
            TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpiryDays));

        if (rotateResult.IsFailure)
        {
            if (rotateResult.Error == AuthErrors.RefreshTokenReused)
            {
                await sessionRevocation.RevokeAllInOwnTransactionAsync(user.Id, ct);
            }

            return rotateResult.Error;
        }

        IList<string> roles = await userManager.GetRolesAsync(user);
        (string? newAccessToken, int expireMinutes) = await jwtProvider.GenerateTokenAsync(user, roles, ct);

        await db.SaveChangesAsync(ct);

        return new AuthResponse(newAccessToken, newRefreshToken, utcNow.AddMinutes(expireMinutes));
    }
}
