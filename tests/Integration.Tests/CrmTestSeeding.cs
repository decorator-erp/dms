using Npgsql;

namespace Integration.Tests;

/// <summary>
/// G9-13's ripple: <c>Project.ClientId</c> now carries a real cross-schema FK to <c>crm.clients(Id)</c>
/// (<c>AddClientForeignKey</c>), so every Integration.Tests flow that creates a project through the real
/// HTTP host needs a real client row first — <c>Guid.NewGuid()</c> no longer survives the insert.
///
/// <para>Raw SQL via <see cref="NpgsqlConnection"/>, not a CRM command handler through DI or HTTP.
/// Written at G9-13, when CRM's own module was migrated into every <see cref="ApiFixture"/> database
/// (<c>ApiFixture.MigrateSchemaAsync</c>'s <c>"crm"</c> case, since G9-07) but not yet registered into
/// the API host's own <c>moduleAssemblies</c> — G9-14 closed that gap (<c>POST /api/clients</c> now
/// exists), but this helper is left as-is rather than switched to an HTTP round-trip: it is correct,
/// every call site already has this fixture's raw connection string in hand, and rewriting working
/// seeding code for style alone risks these tests for no behavioral gain. Same raw-SQL-seeding
/// precedent <c>ProjectAccessGuardTests.SeedEmployeeAsync</c> (Projects.Infrastructure.Tests) already
/// established for a cross-schema dependency.</para>
///
/// <para>Reuses the caller's own <paramref name="appUserId"/> as the client's <c>AppUserId</c> (the FK
/// <c>clients.AppUserId → identity.users.Id</c> from G9-08) rather than registering a second user — the
/// row only needs to exist and satisfy its own FK; nothing in these flows asserts the client is a
/// *different* actor from the employee driving the request.</para>
/// </summary>
internal static class CrmTestSeeding
{
    public static async Task<Guid> SeedClientAsync(string connectionString, string appUserId)
    {
        var clientId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO crm.clients ("Id", "AppUserId", "Address", "IsActive", "CreatedAtUtc")
            VALUES (@id, @appUserId, '1 Client Rd', true, now());
            """;
        command.Parameters.AddWithValue("id", clientId);
        command.Parameters.AddWithValue("appUserId", appUserId);
        await command.ExecuteNonQueryAsync();

        return clientId;
    }
}
