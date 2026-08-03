using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Application.DTOs;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Identity.Application.Features.Auths.Login;

internal sealed record LoginRequest(string Email, string Password);

internal sealed record LoginCommand(LoginRequest Request) : ICommand<AuthResponse>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty();
    }
}

/// <summary>Ported from <c>AuthService.GetTokenAsync</c>. Publishes nothing — login is a read of
/// existing state, not a fact another module could need to react to.</summary>
internal sealed class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    IJwtProvider jwtProvider,
    IdentityModuleDbContext db,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> HandleAsync(LoginCommand command, CancellationToken ct = default)
    {
        var request = command.Request;

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return AuthErrors.InvalidCredentials;

        if (!user.IsActive)
            return AuthErrors.UserNotActive;

        if (!await userManager.CheckPasswordAsync(user, request.Password))
            return AuthErrors.InvalidCredentials;

        var roles = await userManager.GetRolesAsync(user);
        var (token, expireMinutes) = await jwtProvider.GenerateTokenAsync(user, roles, ct);

        var utcNow = DateTime.UtcNow;
        var refreshToken = user.IssueRefreshToken(
            GenerateRefreshTokenValue(), utcNow, TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpiryDays));
        user.UpdateLastLogin();

        await db.SaveChangesAsync(ct);

        return new AuthResponse(token, refreshToken.Token, utcNow.AddMinutes(expireMinutes));
    }

    private static string GenerateRefreshTokenValue() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
}
