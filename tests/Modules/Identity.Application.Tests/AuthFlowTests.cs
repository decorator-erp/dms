using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Auth;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Identity.Application.Tests;

[Collection(IdentityApplicationCollection.Name)]
public sealed class AuthFlowTests(IdentityApplicationFixture fixture)
{
    [Fact]
    public async Task Login_then_refresh_then_revoke_succeeds_and_revoked_token_is_rejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        string suffix = Guid.NewGuid().ToString("N");
        string email = $"user-{suffix}@example.com";
        const string password = IdentityTestSeeding.Password;
        string roleName = $"role-{suffix}";

        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        IdentityResult roleResult = await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));
        Assert.True(roleResult.Succeeded);

        await IdentityTestSeeding.ProvisionActiveUserAsync(sp, email, roleName: roleName);

        ICommandHandler<LoginCommand, AuthResponse> loginHandler = sp.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>();
        Result<AuthResponse> loginResult = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, password)));
        Assert.True(loginResult.IsSuccess);
        AuthResponse firstAuth = loginResult.Value!;

        ICommandHandler<RefreshTokenCommand, AuthResponse> refreshHandler = sp.GetRequiredService<ICommandHandler<RefreshTokenCommand, AuthResponse>>();
        Result<AuthResponse> refreshResult = await refreshHandler.HandleAsync(
            new RefreshTokenCommand(new RefreshTokenRequest(firstAuth.Token, firstAuth.RefreshToken)));
        Assert.True(refreshResult.IsSuccess);
        AuthResponse secondAuth = refreshResult.Value!;
        Assert.NotEqual(firstAuth.RefreshToken, secondAuth.RefreshToken);

        // The first refresh token was rotated away by the call above - presenting it again must fail.
        Result<AuthResponse> reuseOfFirstResult = await refreshHandler.HandleAsync(
            new RefreshTokenCommand(new RefreshTokenRequest(secondAuth.Token, firstAuth.RefreshToken)));
        Assert.True(reuseOfFirstResult.IsFailure);

        ICommandHandler<RevokeRefreshTokenCommand> revokeHandler = sp.GetRequiredService<ICommandHandler<RevokeRefreshTokenCommand>>();
        Result revokeResult = await revokeHandler.HandleAsync(
            new RevokeRefreshTokenCommand(new RefreshTokenRequest(secondAuth.Token, secondAuth.RefreshToken)));
        Assert.True(revokeResult.IsSuccess);

        // Reusing the now-revoked (second) refresh token must also be rejected.
        Result<AuthResponse> reuseOfRevokedResult = await refreshHandler.HandleAsync(
            new RefreshTokenCommand(new RefreshTokenRequest(secondAuth.Token, secondAuth.RefreshToken)));
        Assert.True(reuseOfRevokedResult.IsFailure);
    }

    /// <summary>
    /// Replaces <c>Register_with_duplicate_email_is_rejected</c>, whose subject (self-registration)
    /// no longer exists — duplicate-email rejection is now asserted, with its exact error code, by
    /// <c>Identity.Infrastructure.Tests.UserProvisioningTests</c>.
    ///
    /// <para>What sits here instead is the half of the ADR's invariant that belongs to Identity: a
    /// provisioned account is refused at <c>LoginCommand</c> until something activates it. HR and CRM
    /// assert the other half — that activation happens only via the integration event their business
    /// row commits with. Provisioning is called directly rather than through
    /// <c>IdentityTestSeeding</c>, which activates on purpose.</para>
    /// </summary>
    [Fact]
    public async Task Provisioned_user_cannot_log_in_until_activated()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        string suffix = Guid.NewGuid().ToString("N");
        string email = $"inactive-{suffix}@example.com";
        string roleName = $"role-{suffix}";

        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));

        Result<string> provisioned = await sp.GetRequiredService<IUserProvisioning>().CreateAsync(
            new NewUserRequest(
                email, IdentityTestSeeding.Password, "Test", "User", null, UserKind.Employee, roleName),
            CancellationToken.None);
        Assert.True(provisioned.IsSuccess, provisioned.IsFailure ? provisioned.Error.ToString() : null);

        ICommandHandler<LoginCommand, AuthResponse> loginHandler = sp.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>();
        var credentials = new LoginCommand(new LoginRequest(email, IdentityTestSeeding.Password));

        Result<AuthResponse> beforeActivation = await loginHandler.HandleAsync(credentials);
        Assert.True(beforeActivation.IsFailure, "A provisioned but unactivated user was able to log in.");
        Assert.Equal(AuthErrors.UserNotActive.Code, beforeActivation.Error.Code);

        // POSITIVE CONTROL — the same credentials succeed once activated, so the rejection above is the
        // IsActive gate and not a wrong password, a missing role, or a broken login path.
        Result activated = await sp.GetRequiredService<IUserAdministration>()
            .ReactivateAsync(provisioned.Value!, CancellationToken.None);
        Assert.True(activated.IsSuccess);

        Result<AuthResponse> afterActivation = await loginHandler.HandleAsync(credentials);
        Assert.True(afterActivation.IsSuccess, afterActivation.IsFailure ? afterActivation.Error.ToString() : null);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        string suffix = Guid.NewGuid().ToString("N");
        string email = $"user-{suffix}@example.com";
        string roleName = $"role-{suffix}";

        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));

        await IdentityTestSeeding.ProvisionActiveUserAsync(sp, email, roleName: roleName);

        ICommandHandler<LoginCommand, AuthResponse> loginHandler = sp.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>();
        Result<AuthResponse> loginResult = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, "WrongPassword!")));
        Assert.True(loginResult.IsFailure);
    }

    [Fact]
    public async Task ChangePassword_then_login_with_old_password_is_rejected_and_new_password_succeeds()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        string suffix = Guid.NewGuid().ToString("N");
        string email = $"user-{suffix}@example.com";
        string roleName = $"role-{suffix}";
        const string oldPassword = IdentityTestSeeding.Password;
        const string newPassword = "N3wP@ssw0rd456!";

        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));

        string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(sp, email, roleName: roleName);

        ICommandHandler<ChangePasswordCommand> changePasswordHandler = sp.GetRequiredService<ICommandHandler<ChangePasswordCommand>>();
        Result changeResult = await changePasswordHandler.HandleAsync(
            new ChangePasswordCommand(new ChangePasswordRequest(userId, oldPassword, newPassword)));
        Assert.True(changeResult.IsSuccess);

        ICommandHandler<LoginCommand, AuthResponse> loginHandler = sp.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>();

        Result<AuthResponse> loginWithOldPassword = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, oldPassword)));
        Assert.True(loginWithOldPassword.IsFailure);

        Result<AuthResponse> loginWithNewPassword = await loginHandler.HandleAsync(new LoginCommand(new LoginRequest(email, newPassword)));
        Assert.True(loginWithNewPassword.IsSuccess);
    }
}
