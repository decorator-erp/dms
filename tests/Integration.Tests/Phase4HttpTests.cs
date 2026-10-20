using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
using Hr.Contracts;
using Identity.Contracts;
using Identity.Contracts.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Identity &amp; HR Phase 4 (performance):
/// <list type="bullet">
/// <item><b>Refresh-token row bounds (Aug 4.1, P1, P3, P4, P8):</b> the prune job's selection, its
/// batching and its SQL shape; rotations followed by a prune; the load → prune → rotate → save
/// invariant.</item>
/// <item><b>The session check (P2).</b></item>
/// <item><b>Statement counts</b> for the employee list (4.3) and a user's roles (4.4).</item>
/// </list>
/// The job is resolved by name, as <c>UnlinkedUserReaperTests</c> does, and its settings are changed
/// through <see cref="IConfiguration"/> and restored, which is safe because the collection runs its tests
/// one at a time.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class Phase4HttpTests(ApiFixture fixture)
{
    private const string Section = "Identity:RefreshTokens:";

    /// <summary><c>RefreshTokenOptions</c>' defaults, for putting a setting back.</summary>
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["PruneGraceDays"] = "1",
        ["RevokedRetentionHours"] = "24",
        ["PruneBatchSize"] = "1000",
        ["PruneMaxBatchesPerSweep"] = "10",
    };

    // --- Aug 4.1: prune ---

    [Fact]
    public async Task Prune_deletes_old_expired_and_old_revoked_rows_and_keeps_everything_still_usable()
    {
        string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, NewEmail());
        DateTime now = DateTime.UtcNow;
        string expiredLongAgo = await PlantAsync(userId, created: now.AddDays(-20), expires: now.AddDays(-3), revoked: null);
        string revokedLongAgo = await PlantAsync(userId, created: now.AddDays(-3), expires: now.AddDays(10), revoked: now.AddDays(-2));
        string expiredInsideGrace = await PlantAsync(userId, created: now.AddDays(-15), expires: now.AddHours(-2), revoked: null);
        string revokedInsideWindow = await PlantAsync(userId, created: now.AddHours(-3), expires: now.AddDays(10), revoked: now.AddHours(-1));
        string active = await PlantAsync(userId, created: now, expires: now.AddDays(14), revoked: null);

        await RunPruneAsync();

        HashSet<string> left = await HashesOfAsync(userId);
        Assert.DoesNotContain(expiredLongAgo, left);
        Assert.DoesNotContain(revokedLongAgo, left);
        Assert.Contains(expiredInsideGrace, left);
        Assert.Contains(revokedInsideWindow, left);
        Assert.Contains(active, left);
    }

    /// <summary>P4: batches of 2, at most 2 per sweep. Five prunable rows: the first sweep deletes four in
    /// two batches and stops at the cap, the second deletes the last. The SQL is the batched shape.</summary>
    [Fact]
    public async Task Prune_works_in_batches_and_stops_at_the_per_sweep_cap()
    {
        string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, NewEmail());
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            await PlantAsync(userId, created: now.AddDays(-20), expires: now.AddDays(-5), revoked: null);
        }

        fixture.ClearCapturedSql();
        await RunPruneAsync(("PruneBatchSize", "2"), ("PruneMaxBatchesPerSweep", "2"));
        string[] deletes = [.. fixture.CapturedSql.Where(s => s.Contains("DELETE FROM identity.refresh_tokens", StringComparison.Ordinal))];
        int afterFirst = (await HashesOfAsync(userId)).Count;

        await RunPruneAsync(("PruneBatchSize", "2"), ("PruneMaxBatchesPerSweep", "2"));

        Assert.Equal(2, deletes.Length);
        Assert.All(deletes, sql => Assert.Contains("LIMIT", sql, StringComparison.Ordinal));
        Assert.Equal(1, afterFirst);
        Assert.Empty(await HashesOfAsync(userId));
    }

    /// <summary>P3: the row bound, through the real routes. Fifty rotations leave fifty revoked rows; a
    /// prune with a zero window removes them, leaving the account at or under the cap plus one.</summary>
    [Fact]
    public async Task Fifty_rotations_then_a_prune_leave_the_account_within_the_cap()
    {
        string email = NewEmail();
        string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, email);
        AuthResponse tokens = await LoginAsync(email);
        for (int i = 0; i < 50; i++)
        {
            HttpResponseMessage refreshed = await fixture.CreateClient().PostAsJsonAsync(
                "/api/auth/refresh", new RefreshTokenRequest(tokens.Token, tokens.RefreshToken));
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            tokens = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;
        }

        Assert.Equal(51, (await HashesOfAsync(userId)).Count);

        await RunPruneAsync(("RevokedRetentionHours", "0"));

        Assert.True((await HashesOfAsync(userId)).Count <= 10 + 1);
    }

    /// <summary>P8: a request that loaded the user before a sweep can still save after it. The sweep
    /// deletes a long-revoked row the request has in memory; the request only touches the active one.</summary>
    [Fact]
    public async Task A_user_loaded_before_a_prune_still_rotates_and_saves_after_it()
    {
        string email = NewEmail();
        string userId = await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, email);
        AuthResponse tokens = await LoginAsync(email);
        DateTime now = DateTime.UtcNow;
        await PlantAsync(userId, created: now.AddDays(-3), expires: now.AddDays(10), revoked: now.AddDays(-2));

        await using AsyncServiceScope request = fixture.Services.CreateAsyncScope();
        // Load: a request-path read that puts the user, and so every one of its token rows, in this
        // scope's change tracker.
        Assert.True((await request.ServiceProvider.GetRequiredService<IUserAdministration>()
            .UpdateNameAsync(userId, "Loaded", "First", CancellationToken.None)).IsSuccess);

        await RunPruneAsync();

        // Rotate and save in the same scope: the handler's FindByIdAsync returns the tracked user, whose
        // collection still holds the row the sweep deleted. Only the active token changes.
        Result<AuthResponse> rotated = await request.ServiceProvider
            .GetRequiredService<ICommandHandler<RefreshTokenCommand, AuthResponse>>()
            .HandleAsync(new RefreshTokenCommand(new RefreshTokenRequest(tokens.Token, tokens.RefreshToken)));

        Assert.True(rotated.IsSuccess, rotated.IsFailure ? rotated.Error.ToString() : null);
    }

    /// <summary>A labelled regression guard, not a proof (P3): the owned token rows load in the same
    /// statement as the user, so this count cannot see their number. It catches a new statement being
    /// added to login.</summary>
    [Fact]
    public async Task Regression_guard_login_statement_count_does_not_depend_on_the_token_count()
    {
        string email = NewEmail();
        await IdentityTestSeeding.ProvisionActiveUserAsync(fixture, email);
        await LoginAsync(email);

        fixture.ClearCapturedSql();
        await LoginAsync(email);
        int second = fixture.CapturedSql.Count;

        for (int i = 0; i < 5; i++)
        {
            await LoginAsync(email);
        }

        fixture.ClearCapturedSql();
        await LoginAsync(email);

        Assert.Equal(second, fixture.CapturedSql.Count);
    }

    // --- P2: the session check ---

    [Fact]
    public async Task The_session_check_is_one_statement_and_never_reads_refresh_tokens()
    {
        HttpClient client = TokenClient(HrPermissions.EmployeesRead);

        fixture.ClearCapturedSql();
        await client.GetAsync("/api/employees?pageSize=1");

        string check = Assert.Single(fixture.CapturedSessionChecks);
        Assert.DoesNotContain("refresh_tokens", check, StringComparison.Ordinal);
    }

    // --- 4.3 and 4.4: statement counts ---

    [Fact]
    public async Task The_employee_list_is_three_statements_two_on_hr_employees_whatever_the_page_size()
    {
        for (int i = 0; i < 3; i++)
        {
            await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, NewEmail(), "Counted", "Test", "User");
        }

        HttpClient hr = TokenClient(HrPermissions.EmployeesRead);
        foreach (int pageSize in new[] { 1, 5 })
        {
            fixture.ClearCapturedSql();
            HttpResponseMessage response = await hr.GetAsync($"/api/employees?pageSize={pageSize}&jobTitleFilter=Counted");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string[] sql = [.. fixture.CapturedSql];
            Assert.Equal(3, sql.Length);
            Assert.Equal(2, sql.Count(s => s.Contains("hr.employees", StringComparison.Ordinal)));
            Assert.NotEmpty((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public async Task A_users_roles_are_one_statement()
    {
        TestSession self = await TestUsers.CreateUserInRoleAsync(fixture, IdentityRoles.Employee);

        fixture.ClearCapturedSql();
        HttpResponseMessage response = await self.Client.GetAsync($"/api/roles/users/{self.UserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(fixture.CapturedSql);
    }

    // --- helpers ---

    private async Task RunPruneAsync(params (string Key, string Value)[] settings)
    {
        IConfiguration configuration = fixture.Services.GetRequiredService<IConfiguration>();
        Dictionary<string, string?> originals = settings.ToDictionary(s => s.Key, s => configuration[Section + s.Key]);
        foreach ((string key, string value) in settings)
        {
            configuration[Section + key] = value;
        }

        try
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetServices<IPeriodicJob>()
                .Single(j => j.Name == "RefreshTokenPruneJob")
                .RunAsync(CancellationToken.None);
        }
        finally
        {
            // Back to what it was. A key the host never set goes back to the option's default rather than
            // to null: a null value still binds (as 0), which leaked a zero window into the next test.
            foreach ((string key, string? value) in originals)
            {
                configuration[Section + key] = value ?? Defaults[key];
            }
        }
    }

    private async Task<string> PlantAsync(string userId, DateTime created, DateTime expires, DateTime? revoked)
    {
        string hash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity.refresh_tokens ("TokenHash", "CreatedOn", "ExpiresOn", "RevokedOn", user_id)
            VALUES (@h, @c, @e, @r, @u)
            """;
        command.Parameters.AddWithValue("h", hash);
        command.Parameters.AddWithValue("c", created);
        command.Parameters.AddWithValue("e", expires);
        command.Parameters.AddWithValue("r", (object?)revoked ?? DBNull.Value);
        command.Parameters.AddWithValue("u", userId);
        await command.ExecuteNonQueryAsync();
        return hash;
    }

    private async Task<HashSet<string>> HashesOfAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """SELECT "TokenHash" FROM identity.refresh_tokens WHERE user_id = @u""";
        command.Parameters.AddWithValue("u", userId);
        HashSet<string> hashes = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            hashes.Add(reader.GetString(0));
        }

        return hashes;
    }

    private async Task<AuthResponse> LoginAsync(string email)
    {
        HttpResponseMessage response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, IdentityTestSeeding.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private HttpClient TokenClient(params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(Guid.NewGuid().ToString(), permissions));
        return client;
    }

    private static string NewEmail() => $"p4-{Guid.NewGuid():N}@example.com";
}
