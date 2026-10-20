using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>G1-03 verify: the `identity` schema contains exactly the expected tables after
/// migration — no more, no less (in particular, no shared.outbox_messages/inbox_messages, which are
/// mapped into IdentityModuleDbContext but excluded from its migration).</summary>
[Collection(ApiCollection.Name)]
public sealed class IdentitySchemaTests(ApiFixture fixture)
{
    private static readonly HashSet<string> ExpectedTables =
    [
        "users", "roles", "role_claims", "refresh_tokens",
        "user_claims", "user_roles", "user_logins", "user_tokens",
        "user_permission_overrides",
        // N5: one-time seed steps (BaselineClaimSeeder).
        "seed_markers",
    ];

    [Fact]
    public async Task IdentitySchema_ContainsExactlyTheExpectedTables()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'identity';";

        var actual = new HashSet<string>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actual.Add(reader.GetString(0));
            }
        }

        Assert.Equal(ExpectedTables, actual);
    }
}
