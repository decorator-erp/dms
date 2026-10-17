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
}
