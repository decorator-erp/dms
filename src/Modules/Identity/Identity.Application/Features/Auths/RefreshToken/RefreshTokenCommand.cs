using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Application.DTOs;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Identity.Application.Features.Auths.RefreshToken;

internal sealed record RefreshTokenRequest(string Token, string RefreshToken);

internal sealed record RefreshTokenCommand(RefreshTokenRequest Request) : ICommand<AuthResponse>;

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
/// </summary>
internal sealed class RefreshTokenCommandHandler(
    UserManager<ApplicationUser> userManager,
    IJwtProvider jwtProvider,
    IdentityModuleDbContext db,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> HandleAsync(RefreshTokenCommand command, CancellationToken ct = default)
    {
        var request = command.Request;

        var userId = jwtProvider.ValidateToken(request.Token, validateLifetime: false);
        if (userId is null)
            return AuthErrors.InvalidToken;

        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
            return AuthErrors.InvalidUser;

        var utcNow = DateTime.UtcNow;
        var rotateResult = user.RotateRefreshToken(
            request.RefreshToken,
            GenerateRefreshTokenValue(),
            utcNow,
            TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpiryDays));

        if (rotateResult.IsFailure)
            return rotateResult.Error;

        var roles = await userManager.GetRolesAsync(user);
        var (newAccessToken, expireMinutes) = await jwtProvider.GenerateTokenAsync(user, roles, ct);

        await db.SaveChangesAsync(ct);

        return new AuthResponse(newAccessToken, rotateResult.Value!.Token, utcNow.AddMinutes(expireMinutes));
    }

    private static string GenerateRefreshTokenValue() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
}
