using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 9: every cross-schema foreign key points in an allowed module dependency direction. Needs a
/// real, migrated Postgres to query <c>pg_constraint</c> against — implemented here, per PLAN.md's own
/// text, rather than at G0-07 where no fixture existed yet.
///
/// Passes vacuously today: only <c>shared</c>'s migration has run (no other module exists), and
/// <c>shared.outbox_messages</c>/<c>inbox_messages</c> reference nothing outside their own schema, so
/// the query returns zero rows. Becomes non-vacuous at G2-03, when
/// <c>hr.employees.app_user_id → identity.users(id)</c> is the first real cross-schema FK to check.
///
/// The allow-list is the full ten-module graph from docs/cross-schema-foreign-keys.md, not trimmed to
/// this solution's Identity+HR scope — harmless, since pairs for modules that don't exist yet simply
/// never appear in the query results until those modules are built.
/// </summary>
public sealed class Rule9CrossSchemaForeignKeyTests : IAsyncLifetime
{
    private static readonly (string From, string To)[] AllowedPairs =
    [
        ("hr", "identity"),
        ("crm", "identity"), ("crm", "hr"),
        ("sites", "projects"), ("sites", "crm"),
        ("projects", "identity"), ("projects", "hr"), ("projects", "crm"), ("projects", "approvals"),
        ("finance", "projects"), ("finance", "crm"),
        ("documents", "projects"), ("documents", "hr"), ("documents", "approvals"),
        ("notify", "identity"),
    ];

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:17").Build();
        await _container.StartAsync();

        // Migrate every module schema that exists, in ModuleDependencyOrder.SchemaOrder. Only "shared"
        // has a migration today - add identity/hr's DbContext here as each is built (G1-03, G2-03).
        await using var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
        await shared.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task EveryCrossSchemaForeignKey_PointsInAnAllowedDirection()
    {
        await using var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

        const string sql = """
            SELECT src_ns.nspname AS from_schema, tgt_ns.nspname AS to_schema, con.conname AS constraint_name
            FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid
            JOIN pg_namespace src_ns ON src_ns.oid = src.relnamespace
            JOIN pg_class tgt ON tgt.oid = con.confrelid
            JOIN pg_namespace tgt_ns ON tgt_ns.oid = tgt.relnamespace
            WHERE con.contype = 'f' AND src_ns.nspname <> tgt_ns.nspname;
            """;

        await using var connection = shared.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();

        var violations = new List<string>();
        while (await reader.ReadAsync())
        {
            var from = reader.GetString(0);
            var to = reader.GetString(1);
            var name = reader.GetString(2);

            if (!AllowedPairs.Contains((from, to)))
                violations.Add($"{name}: {from} -> {to}");
        }

        Assert.True(violations.Count == 0,
            $"Cross-schema foreign keys pointing the wrong way: {string.Join(", ", violations)}");
    }
}
