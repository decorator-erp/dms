using System.Reflection;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Notifications.Infrastructure.Tests;

/// <summary>
/// <b>R3 — the raw-SQL schema objects have no other guard.</b>
///
/// <para><c>NotificationsCrossSchemaForeignKeys</c> creates two constraints as
/// <c>migrationBuilder.Sql(...)</c>, because EF cannot emit a foreign key between two different models.
/// <c>dotnet ef migrations add Probe</c> diffs the EF <i>model</i> against the snapshot, and raw SQL is
/// not in the model — so deleting either FK produces an empty probe and a green build. Same reasoning
/// as <c>SitesSchemaObjectTests</c>, which established this discipline.</para>
///
/// <para>The two filtered indexes are a different case and are listed anyway. They ARE in the model
/// (declared with <c>HasFilter</c>, not raw SQL), so a probe would catch their removal — but
/// <c>ux_notifications_dedupe_key</c> is the database half of the idempotency guarantee that the entire
/// consolidated-sweep design rests on, and its <i>filter</i> is what keeps un-keyed notifications from
/// colliding on NULL. That is worth asserting against a real Postgres rather than against a snapshot
/// file.</para>
///
/// <para><b>Fail-closed by construction.</b> <see cref="NoUnexpectedNotifyConstraints"/> asserts nothing
/// exists outside the named lists, so adding a schema object without listing it here goes red. A list
/// that only checked "these exist" would let an unlisted object drift in unnoticed.</para>
/// </summary>
public sealed class NotificationsSchemaObjectTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    private NpgsqlConnection _connection = null!;

    /// <summary>Both raw-SQL cross-schema foreign keys, by name. Rule 9 also asserts these against a
    /// full eight-schema database; this covers them in the module's own suite, where a failure points
    /// at the module that owns them.</summary>
    private static readonly string[] ExpectedForeignKeys =
    [
        "fk_activities_actor_user",
        "fk_notifications_recipient_user",
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "ix_activities_entity",
        "ix_activities_project",
        "ix_notifications_recipient_read",
        "ux_notifications_dedupe_key",
    ];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        // SchemaOrder's own order, and all three links are needed: shared owns audit_logs (which every
        // module's context maps), identity owns the users table both FKs target, notify is what is
        // under test.
        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(connectionString).Options))
        {
            await shared.Database.MigrateAsync();
        }

        await MigrateIdentityAsync(connectionString);

        await using (var notify = new NotificationsDbContext(
            new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connectionString).Options))
        {
            await notify.Database.MigrateAsync();
        }

        _connection = new NpgsqlConnection(connectionString);
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task EveryExpectedForeignKeyExists()
    {
        var actual = await QueryNamesAsync("""
            SELECT c.conname
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'notify' AND c.contype = 'f'
            """);

        Assert.Equal(ExpectedForeignKeys, actual);
    }

    /// <summary>
    /// Both foreign keys are <c>ON DELETE RESTRICT</c>, never CASCADE. The backend cascades
    /// <c>notifications.recipient_user_id</c>; arch rule 9 forbids CASCADE across a schema boundary,
    /// because a delete in one module silently destroying another module's rows is exactly what that
    /// rule exists to prevent. Asserted rather than left to the migration's comment.
    /// </summary>
    [Fact]
    public async Task BothForeignKeys_AreRestrictNotCascade()
    {
        // pg_constraint.confdeltype: 'r' = RESTRICT, 'c' = CASCADE, 'a' = NO ACTION, 'n' = SET NULL.
        // Asserted positively (both are 'r') rather than only negatively (none is 'c'), because a
        // negative-only check would also pass if a constraint were silently dropped altogether.
        var restricting = await QueryNamesAsync("""
            SELECT c.conname
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'notify' AND c.contype = 'f' AND c.confdeltype = 'r'
            """);

        Assert.Equal(ExpectedForeignKeys, restricting);
    }

    [Fact]
    public async Task EveryExpectedIndexExists()
    {
        var actual = await QueryNamesAsync("""
            SELECT indexname FROM pg_indexes
            WHERE schemaname = 'notify' AND indexname NOT LIKE 'PK_%'
            """);

        Assert.Equal(ExpectedIndexes, actual);
    }

    /// <summary>
    /// The dedupe index is UNIQUE and FILTERED, and both halves matter. Unique is what makes re-running
    /// a periodic job a no-op; the <c>IS NOT NULL</c> filter is what stops every un-keyed notification
    /// from colliding with every other on NULL — without it, a user's second one-off notification would
    /// be rejected.
    /// </summary>
    [Fact]
    public async Task DedupeIndex_IsUniqueAndFilteredOnNotNull()
    {
        await using var command = new NpgsqlCommand("""
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'notify' AND indexname = 'ux_notifications_dedupe_key'
            """, _connection);

        var definition = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(definition);
        Assert.Contains("CREATE UNIQUE INDEX", definition);
        Assert.Contains("IS NOT NULL", definition);
    }

    /// <summary>Fail-closed: nothing exists in the notify schema beyond the named lists above.</summary>
    [Fact]
    public async Task NoUnexpectedNotifyConstraints()
    {
        var tables = await QueryNamesAsync("""
            SELECT tablename FROM pg_tables WHERE schemaname = 'notify'
            """);
        Assert.Equal(["activities", "notifications"], tables);

        var checkConstraints = await QueryNamesAsync("""
            SELECT c.conname
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'notify' AND c.contype = 'c'
            """);
        Assert.Empty(checkConstraints);
    }

    /// <summary>
    /// <c>audit_logs</c> lives in <c>shared</c>, not here, even though Notifications serves the audit
    /// endpoints — and <c>ExcludeFromMigrations</c> is what keeps the notify migration from trying to
    /// create a second copy of it. Asserting its absence from this schema is the module-local half of
    /// that split; <c>IdentitySchemaTests</c> makes the same assertion for the outbox tables.
    /// </summary>
    [Fact]
    public async Task NotifySchema_DoesNotContainTheSharedOutboxOrAuditTables()
    {
        var tables = await QueryNamesAsync("""
            SELECT tablename FROM pg_tables WHERE schemaname = 'notify'
            """);

        Assert.DoesNotContain("audit_logs", tables);
        Assert.DoesNotContain("outbox_messages", tables);
        Assert.DoesNotContain("inbox_messages", tables);
    }

    private async Task<List<string>> QueryNamesAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        await using var reader = await command.ExecuteReaderAsync();

        var names = new List<string>();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Migrates Identity through its own <c>IModule.Register</c> on a throwaway
    /// <see cref="ServiceCollection"/>, resolving the context by assembly-qualified name — the exact
    /// technique <c>ApiFixture.MigrateModuleSchemaAsync</c> and <c>Rule9CrossSchemaForeignKeyTests</c>
    /// use, and for the same reason: every module DbContext is internal (rule 4), so no test outside
    /// the module can name the concrete type, and none should be able to.
    /// </summary>
    private static async Task MigrateIdentityAsync(string connectionString)
    {
        var module = ModuleDiscovery.DiscoverModules(Assembly.Load("Identity.Infrastructure")).Single();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        module.Register(services, configuration);

        await using var provider = services.BuildServiceProvider();
        var contextType = Type.GetType(
            "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure")
            ?? throw new InvalidOperationException("IdentityModuleDbContext could not be resolved.");

        using var scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }
}
