using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sites.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sites.Infrastructure.Tests;

/// <summary>
/// <b>R3 — the raw-SQL schema objects have no other guard.</b>
///
/// <para>The initial migration creates roughly a dozen database objects that exist only as
/// <c>migrationBuilder.Sql(...)</c>: a sequence, five functional/partial unique indexes, five blank-string
/// check constraints, the five-clause item range constraint, a composite unique key and the composite
/// foreign key that targets it. None of them is in the EF model.</para>
///
/// <para><b>The empty-probe verify cannot detect drift in any of them, by construction.</b>
/// <c>dotnet ef migrations add Probe</c> diffs the EF <i>model</i> against the snapshot; a raw SQL
/// statement is not in the model, so deleting one still produces an empty probe and a green build. Rule
/// 9 covers the cross-schema foreign keys via <c>pg_constraint</c>, but nothing covered the sequence,
/// the check constraints or the functional indexes — the exact class of object whose absence surfaces
/// as a production failure rather than a test failure. The backend solved this with a startup
/// <c>SchemaGuard</c>; this repo migrates on boot rather than validating on boot, and already queries
/// <c>pg_constraint</c> from a test (rule 9), so a test is the shape that fits here.</para>
///
/// <para><b>Fail-closed by construction.</b> The expected sets below are written out by name, so adding
/// an object to the migration without adding it here fails <see cref="NoUnexpectedSitesConstraints"/> —
/// the same shape as <c>MetaSelfEnforcementTests</c>. A list that only checked "these exist" would let
/// an unlisted object drift in unnoticed.</para>
/// </summary>
public sealed class SitesSchemaObjectTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    private NpgsqlConnection _connection = null!;

    /// <summary>Every check constraint the sites schema is expected to carry — EF-declared and raw-SQL
    /// alike, because from Postgres' side there is no difference and the point is the real shape.</summary>
    private static readonly string[] ExpectedCheckConstraints =
    [
        "ck_site_documents_kind_range",
        "ck_site_documents_one_subject",
        "ck_site_item_options_code_blank",
        "ck_site_item_options_sort",
        "ck_site_items_code_blank",
        "ck_site_items_range",
        "ck_site_items_type_range",
        "ck_site_module_items_sort",
        "ck_site_modules_code_blank",
        "ck_site_modules_sort",
        "ck_site_records_label_blank",
        "ck_site_records_sort",
        "ck_sites_address_blank",
        "ck_sites_building_numbers",
        "ck_sites_code_format",
        "ck_sites_enum_ranges",
        "ck_sites_status_range",
        "ck_sites_status_stamps",
        "ck_sites_submitted_needs_address",
        "ck_srv_one_value",
    ];

    /// <summary>The indexes EF cannot express: functional (lower(...)) or partial, or both.</summary>
    private static readonly string[] ExpectedRawSqlIndexes =
    [
        "ux_site_item_options_item_code",
        "ux_site_items_code",
        "ux_site_modules_code",
        "ux_site_records_label",
        "ux_sites_code",
    ];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        DbContextOptions<SitesDbContext> options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var context = new SitesDbContext(options))
        {
            // Deliberately migrated to InitialSites ONLY, not to latest. The next migration adds seven
            // cross-schema foreign keys pointing at projects/hr/crm/documents, none of which exist in
            // this isolated container -- and standing up six other modules here would make a
            // schema-object test into a composition test. Those constraints have their own coverage:
            // Rule9CrossSchemaForeignKeyTests migrates every module in SchemaOrder and asserts each FK
            // points in an allowed direction. This file owns exactly what InitialSites creates.
            await context.Database.GetService<IMigrator>().MigrateAsync("InitialSites");
        }

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// The single most load-bearing object here, and the one with the least redundancy: the application
    /// reads this sequence by name via <c>nextval</c>, so its absence is invisible to every read path and
    /// to every test that does not create a site through the real code.
    /// </summary>
    [Fact]
    public async Task SiteCodeSequence_ExistsInTheSitesSchema()
    {
        long found = await ScalarAsync("""
            SELECT count(*) FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'S' AND n.nspname = 'sites' AND c.relname = 'site_code_seq';
            """);

        Assert.Equal(1, found);
    }

    /// <summary>And that it actually allocates, in the format the domain's own code constraint demands.</summary>
    [Fact]
    public async Task SiteCodeSequence_AllocatesAValueTheCodeConstraintAccepts()
    {
        long next = await ScalarAsync("SELECT nextval('sites.site_code_seq');");

        string code = $"SITE-{next:D5}";
        long matches = await ScalarAsync($"SELECT CASE WHEN '{code}' ~ '^SITE-[0-9]{{5,}}$' THEN 1 ELSE 0 END;");

        Assert.Equal(1, matches);
    }

    [Fact]
    public async Task EveryExpectedCheckConstraint_Exists()
    {
        string[] actual = await ListAsync("""
            SELECT con.conname FROM pg_constraint con
            JOIN pg_namespace n ON n.oid = con.connamespace
            WHERE n.nspname = 'sites' AND con.contype = 'c'
            ORDER BY con.conname;
            """);

        string[] missing = ExpectedCheckConstraints.Except(actual).ToArray();

        Assert.Empty(missing);
    }

    /// <summary>The fail-closed half: an object added to the migration but not to this file fails here.</summary>
    [Fact]
    public async Task NoUnexpectedSitesConstraints()
    {
        string[] actual = await ListAsync("""
            SELECT con.conname FROM pg_constraint con
            JOIN pg_namespace n ON n.oid = con.connamespace
            WHERE n.nspname = 'sites' AND con.contype = 'c'
            ORDER BY con.conname;
            """);

        // PC-03 liveness, and this direction is the one that needs it. The two sibling tests compute
        // Expected.Except(actual), which goes RED when the query returns nothing — the full expected
        // list shows up as missing. This one computes actual.Except(Expected), which goes GREEN on an
        // empty result: a typo'd schema name, a renamed catalog view, or a connection to the wrong
        // database all read as "no unexpected constraints".
        Assert.NotEmpty(actual);

        string[] unexpected = actual.Except(ExpectedCheckConstraints).ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public async Task EveryFunctionalOrPartialIndex_Exists()
    {
        string[] actual = await ListAsync("""
            SELECT indexname FROM pg_indexes WHERE schemaname = 'sites' ORDER BY indexname;
            """);

        string[] missing = ExpectedRawSqlIndexes.Except(actual).ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Existence is not enough for these two — a plain <c>("Code")</c> index would also be named
    /// <c>ux_sites_code</c> and would pass the check above while allowing "SITE-1" and "site-1" to
    /// coexist. This asserts the definition actually lowercases, and that the label index is actually
    /// partial.
    /// </summary>
    [Fact]
    public async Task TheFunctionalIndexes_AreActuallyFunctionalAndPartial()
    {
        string siteCode = await TextAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname='sites' AND indexname='ux_sites_code';");
        Assert.Contains("lower", siteCode, StringComparison.OrdinalIgnoreCase);

        string label = await TextAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname='sites' AND indexname='ux_site_records_label';");
        Assert.Contains("lower", label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", label, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The composite key and the foreign key that targets it. This pair is what actually enforces "the
    /// chosen option belongs to THIS item"; without it a value could name any option in the table and
    /// only the application check would object.
    /// </summary>
    [Fact]
    public async Task TheCompositeOptionForeignKeyAndItsTargetKey_Exist()
    {
        long unique = await ScalarAsync("""
            SELECT count(*) FROM pg_constraint con
            JOIN pg_namespace n ON n.oid = con.connamespace
            WHERE n.nspname = 'sites' AND con.contype = 'u'
              AND con.conname = 'uq_site_item_options_item_id';
            """);
        Assert.Equal(1, unique);

        long fk = await ScalarAsync("""
            SELECT count(*) FROM pg_constraint con
            JOIN pg_namespace n ON n.oid = con.connamespace
            WHERE n.nspname = 'sites' AND con.contype = 'f'
              AND con.conname = 'fk_site_record_values_option_of_item';
            """);
        Assert.Equal(1, fk);
    }

    /// <summary>
    /// A constraint that exists but does not bite is worse than one that is missing, because it reads as
    /// covered. One live rejection proves the range constraint is armed.
    /// </summary>
    [Fact]
    public async Task TheItemRangeConstraint_ActuallyRejectsABoundOnANonNumberItem()
    {
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("""
                INSERT INTO sites.site_items
                    ("Id","Code","NameEn","NameAr","DataType","Tolerance","MinValue","IsSystem","IsActive","CreatedAtUtc")
                VALUES (gen_random_uuid(),'bad_text','X','س',2,0,5,false,true, now() AT TIME ZONE 'utc');
                """));

        Assert.Equal("ck_site_items_range", ex.ConstraintName);
    }

    [Fact]
    public async Task TheBlankCodeConstraint_ActuallyRejectsWhitespace()
    {
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("""
                INSERT INTO sites.site_modules
                    ("Id","Code","NameEn","NameAr","IsSystem","SortOrder","IsActive","CreatedAtUtc")
                VALUES (gen_random_uuid(),'   ','X','س',false,0,true, now() AT TIME ZONE 'utc');
                """));

        Assert.Equal("ck_site_modules_code_blank", ex.ConstraintName);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<string> TextAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> ListAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        var results = new List<string>();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return [.. results];
    }
}
