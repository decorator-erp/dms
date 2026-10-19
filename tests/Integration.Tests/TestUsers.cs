using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Contracts;
using Identity.Contracts.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Tests;

/// <summary>A logged-in user: the tokens the real <c>POST /api/auth/login</c> issued, and a client that
/// already carries the access token.</summary>
internal sealed record TestSession(
    string UserId, string Email, string AccessToken, string RefreshToken, HttpClient Client);

/// <summary>
/// HR/Identity Phase 0 harness: real users with real tokens, for testing 403/404 through the whole HTTP
/// pipeline.
///
/// <para><b>Why not <see cref="ApiFixture.IssueTestToken"/>.</b> <see cref="TestJwt"/> signs whatever
/// claims it is handed, for a user id that need not exist. That tests route claims and nothing else. Any
/// check that reads the caller's <i>stored</i> state cannot see such a caller: a delegation guard
/// comparing the caller's effective permissions, a security-stamp check, a lockout. Tokens from this
/// class come out of the production <c>LoginCommandHandler</c>, so their permissions are whatever
/// <c>EffectivePermissionService</c> resolves from roles and overrides, and claims added to login later
/// (the security stamp) arrive with no change here.</para>
///
/// <para><b>Setup runs as the seeded admin, over HTTP.</b> Every role a test needs is created, filled
/// and assigned through the public role and permission routes. The caller is <c>admin@system.dms</c>,
/// logged in for real with the password <c>AdminUserSeeder</c> read from configuration. A caller in a
/// full-access role holding every permission passes any authorization rule a later phase adds to those
/// routes. A <see cref="TestJwt"/> token for a random id would start failing the day a delegation guard
/// lands.</para>
///
/// <para>Every user gets a fresh email, and every role a fresh name: <see cref="ApiFixture"/> never
/// resets between test classes, so nothing here may depend on what an earlier test left behind.</para>
/// </summary>
internal static class TestUsers
{
    private const string SeedAdminEmail = "admin@system.dms";

    /// <summary>
    /// Provisions an active user, gives them a fresh role holding exactly <paramref name="permissions"/>
    /// (no role at all when the list is empty), and logs them in.
    /// </summary>
    public static async Task<TestSession> CreateUserWithPermissionsAsync(
        ApiFixture fixture, params string[] permissions)
    {
        string email = NewEmail();
        string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, email);

        // Provisioning puts every user in IdentityRoles.Employee. Take them out of it so the token holds
        // exactly the permissions asked for, whatever that role is granted by then.
        HttpClient admin = await AdminClientAsync(fixture);
        await ExpectAsync(
            HttpStatusCode.NoContent,
            await admin.DeleteAsync($"/api/roles/{await RoleIdAsync(admin, IdentityRoles.Employee)}/users/{userId}"));

        if (permissions.Length > 0)
        {
            string roleId = await CreateRoleAsync(admin, permissions);
            await ExpectAsync(
                HttpStatusCode.NoContent,
                await admin.PostAsync($"/api/roles/{roleId}/users/{userId}", content: null));
        }

        return await LoginAsync(fixture, userId, email, IdentityTestSeeding.Password);
    }

    /// <summary>Provisions an active user in the existing role <paramref name="roleName"/> and logs them in.</summary>
    public static async Task<TestSession> CreateUserInRoleAsync(ApiFixture fixture, string roleName)
    {
        string email = NewEmail();
        string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, email);

        if (roleName != IdentityRoles.Employee)
        {
            HttpClient admin = await AdminClientAsync(fixture);
            await ExpectAsync(
                HttpStatusCode.NoContent,
                await admin.PostAsync($"/api/roles/{await RoleIdAsync(admin, roleName)}/users/{userId}", content: null));
            await ExpectAsync(
                HttpStatusCode.NoContent,
                await admin.DeleteAsync($"/api/roles/{await RoleIdAsync(admin, IdentityRoles.Employee)}/users/{userId}"));
        }

        return await LoginAsync(fixture, userId, email, IdentityTestSeeding.Password);
    }

    /// <summary>Logs in through the real route; asserts success.</summary>
    public static async Task<TestSession> LoginAsync(
        ApiFixture fixture, string userId, string email, string password)
    {
        HttpClient client = fixture.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        await ExpectAsync(HttpStatusCode.OK, response);

        AuthResponse auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        return new TestSession(userId, email, auth.Token, auth.RefreshToken, client);
    }

    /// <summary>A client logged in as the seeded admin.</summary>
    public static async Task<HttpClient> AdminClientAsync(ApiFixture fixture)
    {
        string password = fixture.Services.GetRequiredService<IConfiguration>()["DMS_SEED_ADMIN_PASSWORD"]
            ?? throw new InvalidOperationException(
                "DMS_SEED_ADMIN_PASSWORD is not configured for the test host, so AdminUserSeeder seeded no "
                + "admin and TestUsers has nobody to set roles up as.");

        TestSession admin = await LoginAsync(fixture, string.Empty, SeedAdminEmail, password);
        return admin.Client;
    }

    private static async Task<string> CreateRoleAsync(HttpClient admin, string[] permissions)
    {
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/roles", new { Name = $"harness-{Guid.NewGuid():N}" });
        await ExpectAsync(HttpStatusCode.OK, created);
        string roleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roleId").GetString()!;

        foreach (string permission in permissions)
        {
            await ExpectAsync(
                HttpStatusCode.NoContent,
                await admin.PutAsync($"/api/permissions/roles/{roleId}/{permission}", content: null));
        }

        return roleId;
    }

    private static async Task<string> RoleIdAsync(HttpClient admin, string roleName)
    {
        JsonElement roles = await admin.GetFromJsonAsync<JsonElement>("/api/roles");
        return roles.EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == roleName)
            .GetProperty("id").GetString()!;
    }

    private static string NewEmail() => $"harness-{Guid.NewGuid():N}@example.com";

    private static async Task ExpectAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: expected "
            + $"{(int)expected}, got {(int)response.StatusCode}. {await response.Content.ReadAsStringAsync()}");
}
