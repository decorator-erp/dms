using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>
/// N5 (B6, B7, C5): <see cref="BaselineClaimSeeder"/> gives <c>employee</c> and <c>client</c> their
/// baseline once, behind a marker. The catalog here registers the baseline codes the way HR, CRM and
/// Notifications do in the real host (Identity alone knows none of them), and each test gets a database
/// of its own in one container.
/// </summary>
public sealed class BaselineClaimSeedingTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task A_fresh_database_gets_the_baseline_and_the_marker()
    {
        string cs = await NewDatabaseAsync();

        await SeedModuleAsync(cs);

        Assert.Equal(["employees.view_own", "notifications.view_own"], await ClaimsOfAsync(cs, DefaultRoles.Employee));
        Assert.Equal(
            ["crm.approvals.decide_own", "crm.approvals.view_own", "crm.briefs.view_own", "crm.meetings.request_own",
                "crm.meetings.respond_own", "crm.meetings.view_own", "crm.portal", "notifications.view_own"],
            await ClaimsOfAsync(cs, DefaultRoles.Client));
        Assert.Equal(1L, await ScalarAsync(cs, $"""SELECT count(*) FROM identity.seed_markers WHERE "Key" = '{BaselineClaimSeeder.MarkerKey}'"""));
        Assert.Equal(1L, await ScalarAsync(cs, """SELECT count(*) FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v2'"""));
        Assert.Equal(1L, await ScalarAsync(cs, """SELECT count(*) FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v3'"""));
    }

    /// <summary>B1.4: a database seeded before v3 (v1 and v2 markers, client without the approval
    /// <c>_own</c> codes) gives the client <c>crm.approvals.view_own</c> and <c>crm.approvals.decide_own</c> on
    /// the next start, so a portal client can list and decide its approvals. A v2 claim an admin removed
    /// meanwhile stays removed: v3 re-grants only its own codes.</summary>
    [Fact]
    public async Task A_database_seeded_before_v3_gives_the_client_the_approval_own_codes_on_the_next_start()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs);

        await ExecuteAsync(cs, """DELETE FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v3'""");
        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'client'
              AND rc."ClaimValue" IN ('crm.approvals.view_own', 'crm.approvals.decide_own', 'notifications.view_own')
            """);

        await SeedModuleAsync(cs);

        List<string> client = await ClaimsOfAsync(cs, DefaultRoles.Client);
        Assert.Contains("crm.approvals.view_own", client);
        Assert.Contains("crm.approvals.decide_own", client);
        Assert.DoesNotContain("notifications.view_own", client);
        Assert.Equal(["employees.view_own", "notifications.view_own"], await ClaimsOfAsync(cs, DefaultRoles.Employee));
    }

    /// <summary>B1.4: v3 also runs once. An admin who removes the client's decide permission keeps it removed.</summary>
    [Fact]
    public async Task The_v3_claim_an_admin_removed_stays_removed_on_the_next_start()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs);
        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'client' AND rc."ClaimValue" = 'crm.approvals.decide_own'
            """);

        await SeedModuleAsync(cs);

        Assert.DoesNotContain("crm.approvals.decide_own", await ClaimsOfAsync(cs, DefaultRoles.Client));
    }

    /// <summary>B-16 (I1): a database seeded before v2 existed (v1 marker only, client without
    /// <c>notifications.view_own</c>) gets the client's notification permission on the next start, so a
    /// portal client can read the meeting notifications CRM sends them.</summary>
    [Fact]
    public async Task A_database_seeded_before_v2_gives_the_client_notifications_view_own_on_the_next_start()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs);

        // Back to the state a v1-only host left: the v2 marker and its claim gone, v1 marker present.
        await ExecuteAsync(cs, """DELETE FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v2'""");
        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'client' AND rc."ClaimValue" = 'notifications.view_own'
            """);

        await SeedModuleAsync(cs);

        Assert.Contains("notifications.view_own", await ClaimsOfAsync(cs, DefaultRoles.Client));
        Assert.Equal(["employees.view_own", "notifications.view_own"], await ClaimsOfAsync(cs, DefaultRoles.Employee));
    }

    /// <summary>B-16: v2 also runs once. An admin who removes the client's notification permission after
    /// v2 keeps it removed.</summary>
    [Fact]
    public async Task The_v2_claim_an_admin_removed_stays_removed_on_the_next_start()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs);
        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'client' AND rc."ClaimValue" = 'notifications.view_own'
            """);

        await SeedModuleAsync(cs);

        Assert.DoesNotContain("notifications.view_own", await ClaimsOfAsync(cs, DefaultRoles.Client));
    }

    /// <summary>B7: no reset between the two seeds, the way a real host restarts.</summary>
    [Fact]
    public async Task A_baseline_claim_an_admin_removed_stays_removed_on_the_next_start()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs);

        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'employee' AND rc."ClaimValue" = 'employees.view_own'
            """);

        await SeedModuleAsync(cs);

        Assert.Equal(["notifications.view_own"], await ClaimsOfAsync(cs, DefaultRoles.Employee));
    }

    /// <summary>C5/B6: two instances starting together. One marker, one copy of each claim, and neither
    /// start throws.</summary>
    [Fact]
    public async Task Two_concurrent_seeds_apply_the_baseline_once()
    {
        string cs = await NewDatabaseAsync();
        await using ServiceProvider provider = BuildProvider(cs);
        using (IServiceScope roles = provider.CreateScope())
        {
            await RoleSeeder.SeedAsync(roles.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>());
        }

        await Task.WhenAll(SeedBaselineAsync(provider), SeedBaselineAsync(provider));

        Assert.Equal(3L, await ScalarAsync(cs, "SELECT count(*) FROM identity.seed_markers"));
        Assert.Equal(["employees.view_own", "notifications.view_own"], await ClaimsOfAsync(cs, DefaultRoles.Employee));
        Assert.Equal(8, (await ClaimsOfAsync(cs, DefaultRoles.Client)).Count);
    }

    private static async Task SeedBaselineAsync(ServiceProvider provider)
    {
        using IServiceScope scope = provider.CreateScope();
        await BaselineClaimSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>(),
            scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPermissionCatalog>());
    }

    private static async Task SeedModuleAsync(string cs)
    {
        await using ServiceProvider provider = BuildProvider(cs);
        using IServiceScope scope = provider.CreateScope();
        await ModuleDiscovery.DiscoverModules(typeof(IdentityModule).Assembly).Single().SeedAsync(scope.ServiceProvider);
    }

    private async Task<string> NewDatabaseAsync()
    {
        string name = $"seed_{Guid.NewGuid():N}";
        await ExecuteAsync(_container.GetConnectionString(), $"CREATE DATABASE {name}");
        string cs = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;

        await using (var shared = new SharedDbContext(new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(cs).Options))
        {
            await shared.Database.MigrateAsync();
        }

        await using ServiceProvider provider = BuildProvider(cs);
        using IServiceScope scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>().Database.MigrateAsync();
        return cs;
    }

    private static ServiceProvider BuildProvider(string cs)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = cs,
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "identity-infrastructure-tests",
                ["JwtSettings:Audience"] = "identity-infrastructure-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
                ["DMS_SEED_ADMIN_PASSWORD"] = "SeedTest-P@ssw0rd123!",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        ModuleDiscovery.DiscoverModules(typeof(IdentityModule).Assembly).Single().Register(services, configuration);

        // What HR, Notifications and CRM register in the real host.
        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet("Hr", ["employees.view_own"]));
        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet("Notifications", ["notifications.view_own"]));
        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            "Crm",
            [
                "crm.portal", "crm.briefs.view_own", "crm.meetings.view_own", "crm.meetings.request_own", "crm.meetings.respond_own",
                "crm.approvals.view_own", "crm.approvals.decide_own",
            ]));
        return services.BuildServiceProvider();
    }

    private static async Task<List<string>> ClaimsOfAsync(string cs, string roleName)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT rc."ClaimValue" FROM identity.role_claims rc JOIN identity.roles r ON r."Id" = rc."RoleId"
            WHERE r."Name" = @name ORDER BY rc."ClaimValue"
            """;
        command.Parameters.AddWithValue("name", roleName);
        List<string> values = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string cs, string sql)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Identity.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
