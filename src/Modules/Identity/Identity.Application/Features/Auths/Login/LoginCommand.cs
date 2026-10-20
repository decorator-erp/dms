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
/// existing state, not a fact another module could need to react to.
///
/// <para>K4: the credential check (lockout included) is <see cref="IPasswordSignIn"/>, which commits its
/// failed-attempt bookkeeping in a scope of its own; see that interface for why it must. Unknown email,
/// wrong password and inactive account all answer <c>InvalidCredentials</c>; a locked account answers
/// <c>LockedOut</c>.</para></summary>
internal sealed class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    IPasswordSignIn passwordSignIn,
    IJwtProvider jwtProvider,
    IdentityModuleDbContext db,
    IOptions<JwtOptions> jwtOptions,
    IOptions<RefreshTokenOptions> refreshTokenOptions)
    : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> HandleAsync(LoginCommand command, CancellationToken ct = default)
    {
        LoginRequest request = command.Request;

        PasswordSignInResult signIn = await passwordSignIn.CheckAsync(request.Email, request.Password, ct);
        switch (signIn.Outcome)
        {
            case PasswordSignInOutcome.LockedOut:
                return AuthErrors.LockedOut;
            case PasswordSignInOutcome.InvalidCredentials:
                return AuthErrors.InvalidCredentials;
        }

        ApplicationUser? user = await userManager.FindByIdAsync(signIn.UserId!);
        if (user is null)
        {
            return AuthErrors.InvalidCredentials;
        }

        IList<string> roles = await userManager.GetRolesAsync(user);
        (string? token, int expireMinutes) = await jwtProvider.GenerateTokenAsync(user, roles, ct);

        DateTime utcNow = DateTime.UtcNow;
        string refreshToken = RefreshTokenValues.New();
        // Aug 4.1: at most MaxActivePerUser live sessions; this login ends the oldest one beyond it.
        user.IssueRefreshToken(
            refreshToken, utcNow, TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpiryDays),
            refreshTokenOptions.Value.MaxActivePerUser);
        user.UpdateLastLogin();

        await db.SaveChangesAsync(ct);

        return new AuthResponse(token, refreshToken, utcNow.AddMinutes(expireMinutes));
    }
}
