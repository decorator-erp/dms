using BuildingBlocks.Messaging;
using Identity.Contracts.Auth;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Application.Tests;

[Collection(IdentityApplicationCollection.Name)]
public sealed class AuthFlowTests(IdentityApplicationFixture fixture)
{
    [Fact]
    public async Task Login_then_refresh_then_revoke_succeeds_and_revoked_token_is_rejected()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"user-{suffix}@example.com";
        const string password = "P@ssw0rd123!";
        var roleName = $"role-{suffix}";

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var roleResult = await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));
        Assert.True(roleResult.Succeeded);

        var registerHandler = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await registerHandler.HandleAsync(
            new RegisterCommand(new RegisterRequest(email, password, "Test", "User", null, roleName)));
        Assert.True(registerResult.IsSuccess);

        var loginHandler = sp.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>();
        var loginResult = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, password)));
        Assert.True(loginResult.IsSuccess);
        var firstAuth = loginResult.Value!;

        var refreshHandler = sp.GetRequiredService<ICommandHandler<RefreshTokenCommand, AuthResponse>>();
        var refreshResult = await refreshHandler.HandleAsync(
            new RefreshTokenCommand(new RefreshTokenRequest(firstAuth.Token, firstAuth.RefreshToken)));
        Assert.True(refreshResult.IsSuccess);
        var secondAuth = refreshResult.Value!;
        Assert.NotEqual(firstAuth.RefreshToken, secondAuth.RefreshToken);

        // The first refresh token was rotated away by the call above - presenting it again must fail.
        var reuseOfFirstResult = await refreshHandler.HandleAsync(
            new RefreshTokenCommand(new RefreshTokenRequest(secondAuth.Token, firstAuth.RefreshToken)));
        Assert.True(reuseOfFirstResult.IsFailure);

        var revokeHandler = sp.GetRequiredService<ICommandHandler<RevokeRefreshTokenCommand>>();
        var revokeResult = await revokeHandler.HandleAsync(
            new RevokeRefreshTokenCommand(new RefreshTokenRequest(secondAuth.Token, secondAuth.RefreshToken)));
        Assert.True(revokeResult.IsSuccess);

        // Reusing the now-revoked (second) refresh token must also be rejected.
        var reuseOfRevokedResult = await refreshHandler.HandleAsync(
            new RefreshTokenCommand(new RefreshTokenRequest(secondAuth.Token, secondAuth.RefreshToken)));
        Assert.True(reuseOfRevokedResult.IsFailure);
    }

    [Fact]
    public async Task Register_with_duplicate_email_is_rejected()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"dup-{suffix}@example.com";
        var roleName = $"role-{suffix}";

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));

        var registerHandler = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var request = new RegisterRequest(email, "P@ssw0rd123!", "Test", "User", null, roleName);

        var firstResult = await registerHandler.HandleAsync(new RegisterCommand(request));
        Assert.True(firstResult.IsSuccess);

        var secondResult = await registerHandler.HandleAsync(new RegisterCommand(request));
        Assert.True(secondResult.IsFailure);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"user-{suffix}@example.com";
        var roleName = $"role-{suffix}";

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));

        var registerHandler = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        await registerHandler.HandleAsync(
            new RegisterCommand(new RegisterRequest(email, "P@ssw0rd123!", "Test", "User", null, roleName)));

        var loginHandler = sp.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>();
        var loginResult = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, "WrongPassword!")));
        Assert.True(loginResult.IsFailure);
    }

    [Fact]
    public async Task ChangePassword_then_login_with_old_password_is_rejected_and_new_password_succeeds()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"user-{suffix}@example.com";
        var roleName = $"role-{suffix}";
        const string oldPassword = "P@ssw0rd123!";
        const string newPassword = "N3wP@ssw0rd456!";

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));

        var registerHandler = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await registerHandler.HandleAsync(
            new RegisterCommand(new RegisterRequest(email, oldPassword, "Test", "User", null, roleName)));
        var userId = registerResult.Value!;

        var changePasswordHandler = sp.GetRequiredService<ICommandHandler<ChangePasswordCommand>>();
        var changeResult = await changePasswordHandler.HandleAsync(
            new ChangePasswordCommand(new ChangePasswordRequest(userId, oldPassword, newPassword)));
        Assert.True(changeResult.IsSuccess);

        var loginHandler = sp.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>();

        var loginWithOldPassword = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, oldPassword)));
        Assert.True(loginWithOldPassword.IsFailure);

        var loginWithNewPassword = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, newPassword)));
        Assert.True(loginWithNewPassword.IsSuccess);
    }
}
