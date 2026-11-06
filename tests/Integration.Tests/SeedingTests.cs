using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>H1-03 verify: the module `SeedAsync` startup loop — restored this task after being found
/// commented out in the untracked `Program.cs` → `DI.cs` refactor — actually runs as part of building the
/// real host, not merely that the app boots. Queries `identity.roles` directly rather than resolving
/// `RoleManager&lt;ApplicationRole&gt;` from the container: `ApplicationRole` is internal to
/// Identity.Domain with no `InternalsVisibleTo` grant to this project, and a raw-SQL check is a more
/// direct proof that a seeded row exists than any assertion routed back through application code.</summary>
[Collection(ApiCollection.Name)]
public sealed class SeedingTests(ApiFixture fixture)
{
    [Fact]
    public async Task Startup_SeedsTheFourDefaultRoles()
    {
        // Force the host (and its startup SeedAsync loop) to have run - ApiFixture.InitializeAsync
        // already builds Services, but issuing a real request is the least assumption-laden way to be
        // certain the host is fully up before querying its database directly.
        HttpClient client = fixture.CreateClient();
        await client.GetAsync("/health");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT \"Name\" FROM identity.roles;";

        var seededRoles = new HashSet<string>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                seededRoles.Add(reader.GetString(0));
            }
        }

        // G9-14: "client" joins the other three - DefaultRoles.All's own remarks record why (CRM's
        // Portal is the first real consumer for the role the backend always carried but this port
        // dropped at G0-06 for having no module that needed it yet).
        Assert.Equal(new HashSet<string> { "admin", "manager", "employee", "client" }, seededRoles);
    }

    /// <summary>SOW-4 (R4): the scope-of-work permissions reach admin and manager through
    /// <c>RoleClaimSeeder</c>, which adds every catalog permission a full-access role is missing on each
    /// start, and never reach the client role.</summary>
    [Fact]
    public async Task Full_access_roles_hold_the_scope_permissions_and_the_client_role_holds_none()
    {
        HttpClient client = fixture.CreateClient();
        await client.GetAsync("/health");

        Dictionary<string, HashSet<string>> claims = await RoleClaimsAsync(fixture.ConnectionString);
        string[] scope = ["scope.read", "scope.manage", "scope.confirm"];

        Assert.All(scope, p => Assert.Contains(p, claims["admin"]));
        Assert.All(scope, p => Assert.Contains(p, claims["manager"]));
        Assert.DoesNotContain(claims.GetValueOrDefault("client") ?? [], c => c.StartsWith("scope.", StringComparison.Ordinal));
    }

    /// <summary>C5 (crm-complete, part E): the role → code matrix. The one new code, <c>project.manage_members</c>,
    /// reaches admin and manager through <c>RoleClaimSeeder</c> (full-access roles get every catalog code), as do the
    /// existing <c>stages.manage</c> and <c>stage_tasks.manage</c> that the manual stage and task routes reuse. No
    /// baseline step grants employee or client a Projects write code, so nothing an admin removed comes back.</summary>
    [Fact]
    public async Task Part_e_codes_reach_admin_and_manager_only()
    {
        HttpClient client = fixture.CreateClient();
        await client.GetAsync("/health");

        Dictionary<string, HashSet<string>> claims = await RoleClaimsAsync(fixture.ConnectionString);
        string[] partE = ["project.manage_members", "stages.manage", "stage_tasks.manage"];

        Assert.All(partE, p => Assert.Contains(p, claims["admin"]));
        Assert.All(partE, p => Assert.Contains(p, claims["manager"]));
        foreach (string role in new[] { "employee", "client" })
        {
            Assert.DoesNotContain(claims.GetValueOrDefault(role) ?? [], c => partE.Contains(c, StringComparer.Ordinal));
        }
    }

    /// <summary>C5 (crm-complete, part F): baseline step v4 gives the client role <c>requirements.view_own</c>, the
    /// code behind the portal requirement-items list (F1), and nothing else; admin/manager hold it through
    /// RoleClaimSeeder; employee never gets it. The marker is written, so a later admin removal sticks.</summary>
    [Fact]
    public async Task Part_f_baseline_v4_gives_the_client_requirements_view_own_only()
    {
        HttpClient client = fixture.CreateClient();
        await client.GetAsync("/health");

        Dictionary<string, HashSet<string>> claims = await RoleClaimsAsync(fixture.ConnectionString);
        Assert.Contains("requirements.view_own", claims["client"]);
        Assert.Contains("requirements.view_own", claims["admin"]);
        Assert.DoesNotContain("requirements.view_own", claims.GetValueOrDefault("employee") ?? []);
        Assert.DoesNotContain(claims["client"], c => c is "crm.spaces.manage" or "crm.catalog.manage" or "crm.artifacts.manage" or "crm.briefs.read");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var marker = new NpgsqlCommand("SELECT count(*) FROM identity.seed_markers WHERE \"Key\" = 'baseline-claims-v4'", connection);
        Assert.Equal(1L, (long)(await marker.ExecuteScalarAsync())!);
    }

    /// <summary>Role name → its permission claims, read straight from <c>identity.role_claims</c>.</summary>
    internal static async Task<Dictionary<string, HashSet<string>>> RoleClaimsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT r."Name", rc."ClaimValue"
            FROM identity.role_claims rc JOIN identity.roles r ON r."Id" = rc."RoleId"
            WHERE rc."ClaimValue" IS NOT NULL;
            """;

        var claims = new Dictionary<string, HashSet<string>>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string role = reader.GetString(0);
            if (!claims.TryGetValue(role, out HashSet<string>? set))
            {
                claims[role] = set = [];
            }

            set.Add(reader.GetString(1));
        }

        return claims;
    }
}
