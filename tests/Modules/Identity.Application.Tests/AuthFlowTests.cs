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
    /// <summary>
    /// Every call runs in a scope of its own, as every HTTP request does. Since K4/K5 some state is
    /// written from scopes of its own (the sign-in bookkeeping, the family revocation), and one shared
    /// scope's tracked user would not see it.
    /// </summary>
    [Fact]
    public async Task Login_then_refresh_then_revoke_succeeds_and_revoked_token_is_rejected()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string email = $"user-{suffix}@example.com";
        const string password = IdentityTestSeeding.Password;
        string roleName = $"role-{suffix}";

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            RoleManager<ApplicationRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            IdentityResult roleResult = await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));
            Assert.True(roleResult.Succeeded);
            await IdentityTestSeeding.ProvisionActiveUserAsync(scope.ServiceProvider, email, roleName: roleName);
        }

        Result<AuthResponse> loginResult = await LoginAsync(email, password);
        Assert.True(loginResult.IsSuccess);
        AuthResponse firstAuth = loginResult.Value!;

        Result<AuthResponse> refreshResult = await RefreshAsync(firstAuth.Token, firstAuth.RefreshToken);
        Assert.True(refreshResult.IsSuccess);
        AuthResponse secondAuth = refreshResult.Value!;
        Assert.NotEqual(firstAuth.RefreshToken, secondAuth.RefreshToken);

        Result revokeResult = await SendAsync<RevokeRefreshTokenCommand>(
            new RevokeRefreshTokenCommand(new RefreshTokenRequest(secondAuth.Token, secondAuth.RefreshToken)));
        Assert.True(revokeResult.IsSuccess);

        // Reusing the now-revoked (second) refresh token must be rejected.
        Result<AuthResponse> reuseOfRevokedResult = await RefreshAsync(secondAuth.Token, secondAuth.RefreshToken);
        Assert.True(reuseOfRevokedResult.IsFailure);
    }

    /// <summary>
    /// K5: presenting a refresh token that was already rotated away revokes the whole family. The token
    /// it was rotated to (the legitimate holder's, or the thief's) stops working too, and the
    /// revocation survives the refresh command's own rollback.
    /// </summary>
    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family()
    {
        string email = $"family-{Guid.NewGuid():N}@example.com";
        string roleName = $"role-{Guid.NewGuid():N}";
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>()
                .CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));
            await IdentityTestSeeding.ProvisionActiveUserAsync(scope.ServiceProvider, email, roleName: roleName);
        }

        AuthResponse first = (await LoginAsync(email, IdentityTestSeeding.Password)).Value!;
        AuthResponse second = (await RefreshAsync(first.Token, first.RefreshToken)).Value!;

        Result<AuthResponse> reuse = await RefreshAsync(second.Token, first.RefreshToken);
        Result<AuthResponse> afterReuse = await RefreshAsync(second.Token, second.RefreshToken);

        Assert.Equal(AuthErrors.RefreshTokenReused.Code, reuse.Error.Code);
        Assert.True(afterReuse.IsFailure, "The token the reused one was rotated to still refreshes.");
    }

    private async Task<Result<AuthResponse>> LoginAsync(string email, string password)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<LoginCommand, AuthResponse>>()
            .HandleAsync(new LoginCommand(new LoginRequest(email, password)));
    }

    private async Task<Result<AuthResponse>> RefreshAsync(string accessToken, string refreshToken)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RefreshTokenCommand, AuthResponse>>()
            .HandleAsync(new RefreshTokenCommand(new RefreshTokenRequest(accessToken, refreshToken)));
    }

    private async Task<Result> SendAsync<TCommand>(TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
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

        // K4: an inactive account answers exactly like a wrong password (it used to be UserNotActive,
        // which confirmed to a guesser that the password was right).
        Assert.Equal(AuthErrors.InvalidCredentials.Code, beforeActivation.Error.Code);

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

        // K8: the account is the caller's, so the change runs as that user.
        await using (AsyncServiceScope asUser = fixture.CreateScope(new TestCurrentUser(userId)))
        {
            ICommandHandler<ChangePasswordCommand> changePasswordHandler =
                asUser.ServiceProvider.GetRequiredService<ICommandHandler<ChangePasswordCommand>>();
            Result changeResult = await changePasswordHandler.HandleAsync(
                new ChangePasswordCommand(new ChangePasswordRequest(oldPassword, newPassword)));
            Assert.True(changeResult.IsSuccess, changeResult.IsFailure ? changeResult.Error.ToString() : null);
        }

        Result<AuthResponse> loginWithOldPassword = await LoginAsync(email, oldPassword);
        Assert.True(loginWithOldPassword.IsFailure);

        Result<AuthResponse> loginWithNewPassword = await LoginAsync(email, newPassword);
        Assert.True(loginWithNewPassword.IsSuccess, loginWithNewPassword.IsFailure ? loginWithNewPassword.Error.ToString() : null);
    }
}
