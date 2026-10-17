using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Auth;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Identity.Application.Features.Auths.Login;

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
        LoginRequest request = command.Request;

        ApplicationUser? user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return AuthErrors.InvalidCredentials;
        }
       
        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return AuthErrors.InvalidCredentials;
        }

        if (!user.IsActive)
        {
            return AuthErrors.UserNotActive;
        }

        IList<string> roles = await userManager.GetRolesAsync(user);
        (string? token, int expireMinutes) = await jwtProvider.GenerateTokenAsync(user, roles, ct);

        DateTime utcNow = DateTime.UtcNow;
        Domain.RefreshToken refreshToken = user.IssueRefreshToken(
            GenerateRefreshTokenValue(), utcNow, TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpiryDays));
        user.UpdateLastLogin();

        await db.SaveChangesAsync(ct);

        return new AuthResponse(token, refreshToken.Token, utcNow.AddMinutes(expireMinutes));
    }

    private static string GenerateRefreshTokenValue() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
}
