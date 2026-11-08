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

    /// <summary>Phase 5 (S6, P10): v5 gives <c>employee</c> what a field employee needs on the projects they are
    /// on — read the project, its stages and scope, read and execute tasks, and capture site surveys (read,
    /// create, update, submit; never approve or delete). The role → code matrix after a fresh seed with every
    /// module's catalog: employee gets v1 + v5 and nothing else, client gets none of the v5 codes.</summary>
    [Fact]
    public async Task V5_gives_the_employee_project_task_and_survey_codes_and_the_client_none()
    {
        string cs = await NewDatabaseAsync();

        await SeedModuleAsync(cs, withProjectsAndSites: true);

        Assert.Equal(EmployeeAfterV5, Ordinal(await ClaimsOfAsync(cs, DefaultRoles.Employee)));
        Assert.Equal(
            ["crm.approvals.decide_own", "crm.approvals.view_own", "crm.briefs.view_own", "crm.meetings.request_own",
                "crm.meetings.respond_own", "crm.meetings.view_own", "crm.portal", "notifications.view_own"],
            await ClaimsOfAsync(cs, DefaultRoles.Client));
        Assert.Equal(1L, await ScalarAsync(cs, """SELECT count(*) FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v5'"""));
    }

    /// <summary>v5 on a database seeded through v4: the employee gets the v5 codes on the next start; a v1 code an
    /// admin removed stays removed, and a v5 code removed after v5 ran stays removed too.</summary>
    [Fact]
    public async Task A_database_seeded_before_v5_gets_it_once_and_removals_stay_removed()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs, withProjectsAndSites: true);

        await ExecuteAsync(cs, """DELETE FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v5'""");
        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'employee'
              AND (rc."ClaimValue" LIKE 'project.%' OR rc."ClaimValue" LIKE 'stage%' OR rc."ClaimValue" LIKE 'scope.%'
                   OR rc."ClaimValue" LIKE 'sites.%' OR rc."ClaimValue" = 'employees.view_own')
            """);

        await SeedModuleAsync(cs, withProjectsAndSites: true);
        string[] afterReseed = Ordinal(await ClaimsOfAsync(cs, DefaultRoles.Employee));
        Assert.Equal(EmployeeAfterV5.Where(c => c != "employees.view_own"), afterReseed);

        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'employee' AND rc."ClaimValue" = 'sites.submit'
            """);
        await SeedModuleAsync(cs, withProjectsAndSites: true);
        Assert.DoesNotContain("sites.submit", await ClaimsOfAsync(cs, DefaultRoles.Employee));
    }

    /// <summary>BF-1: v6 gives the employee what a surveyor's lookups read — the project's spaces
    /// (<c>crm.spaces.read</c>, already project-scoped by CRM), the catalog items (<c>crm.catalog.read</c>, global
    /// reference data) and the project's documents (<c>documents.read</c>/<c>documents.upload</c>, project-scoped by
    /// <c>DocumentsScopeDecorator</c>, writes Editor and up). Nothing else: not the CRM manage codes, not
    /// <c>documents.update</c>/<c>delete</c>, not <c>employees.read</c>; and the client gets none of it.</summary>
    [Fact]
    public async Task V6_gives_the_employee_the_survey_lookup_codes_and_the_client_none()
    {
        string cs = await NewDatabaseAsync();

        await SeedModuleAsync(cs, withProjectsAndSites: true, withSurveyLookups: true);

        Assert.Equal(EmployeeAfterV6, Ordinal(await ClaimsOfAsync(cs, DefaultRoles.Employee)));
        Assert.Equal(
            ["crm.approvals.decide_own", "crm.approvals.view_own", "crm.briefs.view_own", "crm.meetings.request_own",
                "crm.meetings.respond_own", "crm.meetings.view_own", "crm.portal", "notifications.view_own"],
            await ClaimsOfAsync(cs, DefaultRoles.Client));
        Assert.Equal(1L, await ScalarAsync(cs, """SELECT count(*) FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v6'"""));
    }

    /// <summary>v6 on a database seeded through v5: the employee gets exactly the four v6 codes on the next start; a
    /// v5 code an admin removed stays removed (v6 re-grants only its own codes), and a v6 code removed after v6 ran
    /// stays removed too.</summary>
    [Fact]
    public async Task A_database_seeded_before_v6_gets_it_once_and_removals_stay_removed()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs, withProjectsAndSites: true, withSurveyLookups: true);

        await ExecuteAsync(cs, """DELETE FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v6'""");
        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'employee'
              AND (rc."ClaimValue" LIKE 'crm.%' OR rc."ClaimValue" LIKE 'documents.%' OR rc."ClaimValue" = 'sites.submit')
            """);

        await SeedModuleAsync(cs, withProjectsAndSites: true, withSurveyLookups: true);
        Assert.Equal(EmployeeAfterV6.Where(c => c != "sites.submit"), Ordinal(await ClaimsOfAsync(cs, DefaultRoles.Employee)));

        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'employee' AND rc."ClaimValue" = 'documents.upload'
            """);
        await SeedModuleAsync(cs, withProjectsAndSites: true, withSurveyLookups: true);
        Assert.DoesNotContain("documents.upload", await ClaimsOfAsync(cs, DefaultRoles.Employee));
    }

    /// <summary>P2-A: v7 gives the client the two portal-visibility read codes — the files (documents, gallery,
    /// streams) and the progress (overview, timeline, tasks, updates). The section switches and the shares still
    /// decide what each route returns. Never the office's <c>crm.portal.manage</c>, and the employee gets none of it.</summary>
    [Fact]
    public async Task V7_gives_the_client_the_portal_visibility_codes_and_the_employee_none()
    {
        string cs = await NewDatabaseAsync();

        await SeedModuleAsync(cs, withPortalVisibility: true);

        Assert.Equal(ClientAfterV7, Ordinal(await ClaimsOfAsync(cs, DefaultRoles.Client)));
        Assert.Equal(["employees.view_own", "notifications.view_own"], await ClaimsOfAsync(cs, DefaultRoles.Employee));
        Assert.Equal(1L, await ScalarAsync(cs, """SELECT count(*) FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v7'"""));
    }

    /// <summary>v7 on a database seeded through v6: the client gets exactly the two v7 codes on the next start; a
    /// v1 code an admin removed stays removed (v7 re-grants only its own codes), and a v7 code removed after v7 ran
    /// stays removed too.</summary>
    [Fact]
    public async Task A_database_seeded_before_v7_gets_it_once_and_removals_stay_removed()
    {
        string cs = await NewDatabaseAsync();
        await SeedModuleAsync(cs, withPortalVisibility: true);

        await ExecuteAsync(cs, """DELETE FROM identity.seed_markers WHERE "Key" = 'baseline-claims-v7'""");
        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'client'
              AND (rc."ClaimValue" LIKE 'crm.portal.%' OR rc."ClaimValue" = 'crm.briefs.view_own')
            """);

        await SeedModuleAsync(cs, withPortalVisibility: true);
        Assert.Equal(ClientAfterV7.Where(c => c != "crm.briefs.view_own"), Ordinal(await ClaimsOfAsync(cs, DefaultRoles.Client)));

        await ExecuteAsync(cs, """
            DELETE FROM identity.role_claims rc USING identity.roles r
            WHERE rc."RoleId" = r."Id" AND r."Name" = 'client' AND rc."ClaimValue" = 'crm.portal.files.view_own'
            """);
        await SeedModuleAsync(cs, withPortalVisibility: true);
        Assert.DoesNotContain("crm.portal.files.view_own", await ClaimsOfAsync(cs, DefaultRoles.Client));
    }

    private static string[] ClientAfterV7 => Ordinal(
    [
        "crm.approvals.decide_own", "crm.approvals.view_own", "crm.briefs.view_own", "crm.meetings.request_own",
        "crm.meetings.respond_own", "crm.meetings.view_own", "crm.portal", "notifications.view_own",
        "crm.portal.files.view_own", "crm.portal.progress.view_own",
    ]);

    private static string[] EmployeeAfterV6 => Ordinal(
        [.. EmployeeAfterV5, "crm.catalog.read", "crm.spaces.read", "documents.read", "documents.upload"]);

    /// <summary>The database orders by its collation; the matrix is compared in ordinal order.</summary>
    private static string[] Ordinal(IEnumerable<string> codes) => [.. codes.Order(StringComparer.Ordinal)];

    private static readonly string[] EmployeeAfterV5 =
    [
        "employees.view_own", "notifications.view_own", "project.read", "scope.read", "sites.create", "sites.read",
        "sites.submit", "sites.update", "stage_tasks.complete", "stage_tasks.read", "stages.read",
    ];

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

        Assert.Equal(7L, await ScalarAsync(cs, "SELECT count(*) FROM identity.seed_markers")); // v1-v7 (crm-complete F adds v4, Sites/Projects phase 5 adds v5, BF-1 adds v6, P2-A adds v7)
        Assert.Equal(["employees.view_own", "notifications.view_own"], await ClaimsOfAsync(cs, DefaultRoles.Employee));
        Assert.Equal(8, (await ClaimsOfAsync(cs, DefaultRoles.Client)).Count); // v4's requirements.view_own is not in this catalog, so it is skipped
    }

    private static async Task SeedBaselineAsync(ServiceProvider provider)
    {
        using IServiceScope scope = provider.CreateScope();
        await BaselineClaimSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>(),
            scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPermissionCatalog>());
    }

    private static async Task SeedModuleAsync(
        string cs, bool withProjectsAndSites = false, bool withSurveyLookups = false, bool withPortalVisibility = false)
    {
        await using ServiceProvider provider = BuildProvider(cs, withProjectsAndSites, withSurveyLookups, withPortalVisibility);
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

    private static ServiceProvider BuildProvider(
        string cs, bool withProjectsAndSites = false, bool withSurveyLookups = false, bool withPortalVisibility = false)
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
        if (withProjectsAndSites)
        {
            // Every code the two modules register, so the matrix shows what v5 grants and what it leaves out.
            services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
                "Projects",
                [
                    "project.read", "project.read_all", "project.create", "project.update", "project.set_active",
                    "project.change_status", "project.delete", "project.manage_members", "stages.read", "stages.manage",
                    "stages.complete", "stages.approve", "stages.reject", "stages.manage_gates", "stage_teams.manage",
                    "scope.read", "scope.manage", "scope.confirm", "stage_tasks.read", "stage_tasks.manage",
                    "stage_tasks.assign", "stage_tasks.complete", "stage_tasks.request_approval",
                    "stage_tasks.decide_approval", "stage_tasks.manage_gates",
                ]));
            services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
                "Sites",
                ["sites.read", "sites.create", "sites.update", "sites.submit", "sites.approve", "sites.delete", "sites.definitions.manage"]));
        }

        if (withSurveyLookups)
        {
            // BF-1: the CRM lookup and manage codes and every Documents code, so the matrix shows that v6 grants
            // the two reads and the two document codes and leaves the rest out.
            services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
                "CrmLookups",
                ["crm.spaces.read", "crm.spaces.manage", "crm.catalog.read", "crm.catalog.manage"]));
            services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
                "Documents",
                ["documents.read", "documents.upload", "documents.update", "documents.delete", "file_templates.read",
                    "file_templates.manage"]));
        }

        if (withPortalVisibility)
        {
            // P2-A: the two client codes v7 grants and the office manage code it must leave out.
            services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
                "CrmPortalVisibility",
                ["crm.portal.manage", "crm.portal.files.view_own", "crm.portal.progress.view_own"]));
        }

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
