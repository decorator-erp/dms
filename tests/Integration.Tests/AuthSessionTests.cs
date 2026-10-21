using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Identity.Contracts;
using Identity.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// HR/Identity Phase 1B, through the whole HTTP pipeline with real login tokens:
/// <list type="bullet">
/// <item>K4: lockout, and identical answers for unknown, wrong-password and inactive logins;</item>
/// <item>K5: family revocation on reuse, revoke on password change, logout, hashed storage;</item>
/// <item>K8: change-password acts on the caller only;</item>
/// <item>N2: the security stamp;</item>
/// <item>Aug 3.10: a malformed token is a 401.</item>
/// </list>
/// The shared host runs with a session-state cache TTL of 0 and very high auth rate limits
/// (<see cref="ApiFixture"/>). The rate limits themselves are <see cref="AuthRateLimitTests"/>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthSessionTests(ApiFixture fixture)
{
    private const string WrongPassword = "Wr0ng!Password";

    // --- K4: login ---

    [Fact]
    public async Task UnknownEmail_WrongPassword_AndInactiveAccount_AnswerIdentically()
    {
        TestSession active = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        string inactiveEmail = await ProvisionInactiveUserAsync();

        string unknown = await LoginBodyAsync($"nobody-{Guid.NewGuid():N}@example.com", IdentityTestSeeding.Password);
        string wrong = await LoginBodyAsync(active.Email, WrongPassword);
        // The RIGHT password on an inactive account: the case that used to answer UserNotActive.
        string inactive = await LoginBodyAsync(inactiveEmail, IdentityTestSeeding.Password);

        Assert.Equal(unknown, wrong);
        Assert.Equal(unknown, inactive);
        Assert.Contains("authentications.InvalidCredentials", unknown);
    }

    [Fact]
    public async Task AfterFiveFailures_TheRightPasswordIsRefused_WithLockedOut()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            await PostLoginAsync(user.Email, WrongPassword);
        }

        HttpResponseMessage rightPassword = await PostLoginAsync(user.Email, IdentityTestSeeding.Password);
        string body = await rightPassword.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, rightPassword.StatusCode);
        Assert.Contains("authentications.LockedOut", body);
    }

    [Fact]
    public async Task ASuccessfulLogin_ResetsTheFailureCount()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture);

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            await PostLoginAsync(user.Email, WrongPassword);
        }

        Assert.Equal(HttpStatusCode.OK, (await PostLoginAsync(user.Email, IdentityTestSeeding.Password)).StatusCode);

        // Four more failures: eight in total, but never five in a row, so no lockout.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            await PostLoginAsync(user.Email, WrongPassword);
        }

        Assert.Equal(HttpStatusCode.OK, (await PostLoginAsync(user.Email, IdentityTestSeeding.Password)).StatusCode);
    }

    // --- K5: refresh tokens ---

    [Fact]
    public async Task PresentingARotatedRefreshToken_RevokesTheFamily()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        AuthResponse rotated = await RefreshOkAsync(user.AccessToken, user.RefreshToken);

        HttpResponseMessage replay = await RefreshAsync(rotated.Token, user.RefreshToken);
        HttpResponseMessage legitimate = await RefreshAsync(rotated.Token, rotated.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains("authentications.RefreshTokenReused", await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);
    }

    [Fact]
    public async Task ARefreshTokenFromBeforeAPasswordChange_IsRejected()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        TestSession otherDevice = await TestUsers.LoginAsync(fixture, user.UserId, user.Email, IdentityTestSeeding.Password);

        HttpResponseMessage change = await user.Client.PostAsJsonAsync(
            "/api/auth/change-password", new { CurrentPassword = IdentityTestSeeding.Password, NewPassword = "N3w!Passw0rd" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(otherDevice.AccessToken, otherDevice.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(user.AccessToken, user.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesEveryRefreshToken_AndEndsTheLiveAccessToken()
    {
        TestSession first = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);
        TestSession second = await TestUsers.LoginAsync(fixture, first.UserId, first.Email, IdentityTestSeeding.Password);

        HttpResponseMessage logout = await first.Client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first.AccessToken, first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.AccessToken, second.RefreshToken)).StatusCode);
        await AssertStaleAsync(await second.Client.GetAsync("/api/roles"));
    }

    [Fact]
    public async Task NoRefreshTokenValueIsStored_OnlyItsSha256()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture);

        List<string> stored = await StoredTokenHashesAsync(user.UserId);

        Assert.NotEmpty(stored);
        Assert.DoesNotContain(user.RefreshToken, stored);
        Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(user.RefreshToken))), stored);
    }

    // --- K8: change-password is the caller's ---

    [Fact]
    public async Task ChangePassword_IgnoresAnyUserIdInTheBody()
    {
        TestSession caller = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        TestSession other = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        const string newPassword = "N3w!Passw0rd";

        // The old request shape, naming somebody else. The field no longer exists, so it is ignored.
        HttpResponseMessage response = await caller.Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            other.UserId,
            CurrentPassword = IdentityTestSeeding.Password,
            NewPassword = newPassword,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostLoginAsync(caller.Email, newPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostLoginAsync(other.Email, IdentityTestSeeding.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostLoginAsync(other.Email, newPassword)).StatusCode);
    }

    // --- N2: the security stamp ---

    [Fact]
    public async Task DeactivatingAUser_EndsTheirLiveToken_AndTheirRefresh()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/roles")).StatusCode);

        // What HR's deactivation event handler calls.
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            Result deactivated = await scope.ServiceProvider.GetRequiredService<IUserAdministration>()
                .DeactivateAsync(user.UserId, CancellationToken.None);
            Assert.True(deactivated.IsSuccess);
        }

        await AssertStaleAsync(await user.Client.GetAsync("/api/roles"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(user.AccessToken, user.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task AssigningARole_MakesTheOldTokenStale_AndRefreshCarriesTheNewPermissions()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string roleId = await TestUsers.CreateRoleAsync(admin, IdentityPermissions.UsersRead);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/roles/{roleId}/users/{user.UserId}", null)).StatusCode);

        await AssertStaleAsync(await user.Client.GetAsync("/api/roles"));

        AuthResponse refreshed = await RefreshOkAsync(user.AccessToken, user.RefreshToken);
        HttpClient renewed = fixture.CreateClient();
        renewed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.Token);
        JsonNode me = JsonNode.Parse(await renewed.GetStringAsync("/api/permissions/me/effective"))!;

        Assert.Contains(IdentityPermissions.UsersRead, me["permissions"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.OK, (await renewed.GetAsync("/api/roles")).StatusCode);
    }

    [Fact]
    public async Task EveryAuthenticatedRequest_ChecksTheSession()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);

        fixture.ClearCapturedSql();
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/roles")).StatusCode);

        // The statement ApiFixture.CapturedSql leaves out, so the schema-isolation tests are not
        // excluding something that never ran.
        Assert.NotEmpty(fixture.CapturedSessionChecks);
    }

    // --- Aug 3.10: malformed tokens ---

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("oversized")]
    public async Task AMalformedOrOversizedAccessToken_OnRefresh_Is401_Not500(string token)
    {
        // JwtSecurityTokenHandler refuses anything over 250 KB with an ArgumentException.
        string presented = token == "oversized" ? new string('a', 300_000) : token;

        HttpResponseMessage response = await RefreshAsync(presented, "irrelevant");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("authentications.InvalidToken", await response.Content.ReadAsStringAsync());
    }

    // --- helpers ---

    private Task<HttpResponseMessage> PostLoginAsync(string email, string password) =>
        fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    /// <summary>The 401 body, with the one per-request field (the trace id) taken out.</summary>
    private async Task<string> LoginBodyAsync(string email, string password)
    {
        HttpResponseMessage response = await PostLoginAsync(email, password);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        JsonObject body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    private Task<HttpResponseMessage> RefreshAsync(string accessToken, string refreshToken) =>
        fixture.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(accessToken, refreshToken));

    private async Task<AuthResponse> RefreshOkAsync(string accessToken, string refreshToken)
    {
        HttpResponseMessage response = await RefreshAsync(accessToken, refreshToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task AssertStaleAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 StaleToken, got {(int)response.StatusCode}: {body}");
        Assert.Contains("authentications.StaleToken", body);
    }

    private async Task<string> ProvisionInactiveUserAsync()
    {
        string email = $"inactive-{Guid.NewGuid():N}@example.com";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        Result<string> provisioned = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>().CreateAsync(
            new NewUserRequest(email, IdentityTestSeeding.Password, "Inactive", "User", null, UserKind.Employee, IdentityRoles.Employee),
            CancellationToken.None);
        Assert.True(provisioned.IsSuccess);
        return email;
    }

    private async Task<List<string>> StoredTokenHashesAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """SELECT "TokenHash" FROM identity.refresh_tokens WHERE user_id = @id""";
        command.Parameters.AddWithValue("id", userId);
        List<string> hashes = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            hashes.Add(reader.GetString(0));
        }

        return hashes;
    }
}
