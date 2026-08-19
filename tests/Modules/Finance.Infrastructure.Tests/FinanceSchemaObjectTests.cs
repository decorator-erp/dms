using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Finance.Infrastructure.Tests;

/// <summary>
/// <b>R3 — the named-list guard, applied to what Postgres actually created.</b>
///
/// <para><b>What this covers that the empty-probe verify does not, stated precisely.</b> Finance has no
/// raw-SQL objects in its initial migration, so unlike <c>SitesSchemaObjectTests</c> that is not the gap
/// being closed here. The plan for this task said money precision is invisible to the probe "exactly as
/// raw SQL is", and that is <b>not accurate</b> — <c>HasColumnType</c> is part of the EF model, so a
/// configuration edited without regenerating the migration does produce a non-empty probe. The real gap
/// is narrower and still worth a test:
/// <list type="bullet">
/// <item>A <i>coordinated</i> edit — configuration changed and migration regenerated together — leaves
/// the model and snapshot agreeing with each other and produces an empty probe, while every deployed
/// column silently changes scale.</item>
/// <item>The probe compares model to snapshot. It never asks the database anything, so it cannot tell
/// you what the schema in front of you actually holds.</item>
/// <item>Nothing else asserts the <i>absence</i> of objects. The fail-closed halves below are what stop
/// an index or a constraint drifting in unlisted.</item>
/// </list></para>
///
/// <para><b>Fail-closed by construction.</b> Each expected set is written out by name, and each has a
/// matching "nothing outside this list" test. The two directions fail differently and both are needed:
/// <c>Expected.Except(actual)</c> goes red when the query returns nothing, while
/// <c>actual.Except(Expected)</c> goes <i>green</i> on an empty result — a typo'd schema name, a renamed
/// catalog view or a connection to the wrong database all read as "nothing unexpected". Every test of
/// the second shape therefore carries its own liveness assertion.</para>
/// </summary>
public sealed class FinanceSchemaObjectTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    private NpgsqlConnection _connection = null!;

    /// <summary>Both check constraints on <c>variations</c>, and there are no others in the schema.
    /// They are the database half of the kind discriminator — see <c>VariationConfiguration</c> for why
    /// they compare ints here and strings in the backend.</summary>
    private static readonly string[] ExpectedCheckConstraints =
    [
        "ck_variations_client_diff",
        "ck_variations_internal",
    ];

    /// <summary>Every index the initial migration creates, primary keys excluded (those are asserted
    /// separately, since their names are EF-generated rather than chosen).</summary>
    private static readonly string[] ExpectedIndexes =
    [
        "ix_boq_snapshot_lines_boq_snapshot_id",
        "ix_boq_snapshot_lines_invoice_line_id",
        "ix_boq_snapshot_lines_project_task_id",
        "ix_boq_snapshot_lines_variation_id",
        "ix_boq_snapshots_project_id_version",
        "ix_boq_snapshots_project_stage_id",
        "ix_budget_lines_cost_category_id",
        "ix_budget_lines_project_id",
        "ix_budget_lines_project_stage_id",
        "ix_budget_lines_project_task_id",
        "ix_cost_categories_code",
        "ix_invoice_lines_budget_line_id",
        "ix_invoice_lines_invoice_id",
        "ix_invoice_lines_project_task_id",
        "ix_invoices_client_id",
        "ix_invoices_due_on",
        "ix_invoices_number",
        "ix_invoices_project_id_status",
        "ix_invoices_project_stage_id",
        "ix_ledger_allocations_invoice_id",
        "ix_ledger_allocations_ledger_entry_id",
        "ix_ledger_entries_budget_line_id",
        "ix_ledger_entries_client_id",
        "ix_ledger_entries_project_id_occurred_on",
        "ix_ledger_entries_project_stage_id",
        "ix_ledger_entries_project_task_id",
        "ix_ledger_entries_reversal_of_id",
        "ix_ledger_entries_type",
        "ix_ledger_entries_variation_id",
        "ix_variations_budget_line_id",
        "ix_variations_kind",
        "ix_variations_project_id_status",
        "ix_variations_project_stage_id",
        "ix_variations_project_task_id",
        "ix_variations_signed_by_client_id",
    ];

    /// <summary>The nine tables, and the two indexes that must be UNIQUE — existence alone would not
    /// distinguish a plain index from a unique one, and it is the uniqueness that carries the rule.</summary>
    private static readonly string[] ExpectedTables =
    [
        "boq_snapshot_lines", "boq_snapshots", "budget_lines", "cost_categories", "invoice_lines",
        "invoices", "ledger_allocations", "ledger_entries", "variations",
    ];

    private static readonly string[] ExpectedUniqueIndexes =
    [
        "ix_boq_snapshots_project_id_version",
        "ix_cost_categories_code",
        "ix_invoices_number",
    ];

    /// <summary>All 22 money columns, by table and column, recounted from the entities rather than
    /// taken from the plan. <c>cost_categories</c> is the one finance table with none.</summary>
    private static readonly string[] ExpectedMoneyColumns =
    [
        "boq_snapshot_lines.ActualAmount",
        "boq_snapshot_lines.BudgetedAmount",
        "boq_snapshot_lines.DifferenceAmount",
        "boq_snapshot_lines.Quantity",
        "boq_snapshots.ActualTotal",
        "boq_snapshots.BudgetedTotal",
        "boq_snapshots.DifferenceTotal",
        "boq_snapshots.PurchaseTotal",
        "budget_lines.ActualAmount",
        "budget_lines.AdjustmentAllocation",
        "budget_lines.AllocatedAmount",
        "budget_lines.CommittedAmount",
        "invoice_lines.LineTotal",
        "invoice_lines.Quantity",
        "invoice_lines.UnitPrice",
        "invoices.PaidAmount",
        "invoices.TotalAmount",
        "ledger_allocations.Amount",
        "ledger_entries.Amount",
        "variations.Amount",
        "variations.BudgetedAmount",
        "variations.ChosenAmount",
    ];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var context = new FinanceDbContext(options))
        {
            // Deliberately migrated to InitialFinance ONLY, not to latest -- the same pin
            // SitesSchemaObjectTests uses, and F1-05 is what made it necessary. The next migration adds
            // 22 cross-schema foreign keys pointing at projects/crm/hr, none of which exist in this
            // isolated container, and standing up eight other modules here would turn a schema-object
            // test into a composition test. Those constraints have their own coverage:
            // Rule9CrossSchemaForeignKeyTests migrates every module in SchemaOrder and asserts all 22
            // by name. This file owns exactly what InitialFinance creates.
            await context.Database.GetService<IMigrator>().MigrateAsync("InitialFinance");
        }

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task EveryExpectedTable_Exists()
    {
        var actual = await TablesAsync();

        Assert.Empty(ExpectedTables.Except(actual));
    }

    [Fact]
    public async Task NoUnexpectedFinanceTables()
    {
        var actual = await TablesAsync();

        // Liveness: this direction goes green on an empty result, so the query has to be proven to
        // return something before "nothing unexpected" means anything.
        Assert.NotEmpty(actual);

        Assert.Empty(actual.Except(ExpectedTables));
    }

    [Fact]
    public async Task EveryExpectedCheckConstraint_Exists()
    {
        var actual = await CheckConstraintsAsync();

        Assert.Empty(ExpectedCheckConstraints.Except(actual));
    }

    [Fact]
    public async Task NoUnexpectedFinanceCheckConstraints()
    {
        var actual = await CheckConstraintsAsync();

        Assert.NotEmpty(actual);

        Assert.Empty(actual.Except(ExpectedCheckConstraints));
    }

    [Fact]
    public async Task EveryExpectedIndex_Exists()
    {
        var actual = await IndexesAsync();

        Assert.Empty(ExpectedIndexes.Except(actual));
    }

    /// <summary>The fail-closed half. Primary keys are allowed through by name shape rather than being
    /// listed: EF generates them as <c>PK_&lt;table&gt;</c>, so listing them would only restate the
    /// table list one line lower down.</summary>
    [Fact]
    public async Task NoUnexpectedFinanceIndexes()
    {
        var actual = await IndexesAsync();

        Assert.NotEmpty(actual);

        var unexpected = actual
            .Where(name => !ExpectedIndexes.Contains(name))
            .Where(name => !name.StartsWith("PK_", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unexpected);
    }

    /// <summary>
    /// Existence is not enough for these three. A non-unique index would carry the same name and pass
    /// the test above while allowing two invoices to share a number, two cost categories to share a
    /// code, or two BOQ snapshots to claim the same version of the same project — the last being the
    /// race the index exists to lose safely.
    /// </summary>
    [Fact]
    public async Task TheThreeUniqueIndexes_AreActuallyUnique()
    {
        var actual = await ListAsync("""
            SELECT c.relname FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'finance' AND i.indisunique AND NOT i.indisprimary
            ORDER BY c.relname;
            """);

        Assert.Equal(ExpectedUniqueIndexes, actual);
    }

    /// <summary>
    /// <b>The money-precision guard.</b> Asserts what Postgres holds, not what the EF model claims — see
    /// the type-level note for why the empty probe does not cover this.
    ///
    /// <para>Both directions in one test, deliberately: the exact set is asserted, so a money column
    /// that loses its precision, gains the wrong one, or disappears entirely all fail here, and a new
    /// decimal column added without <c>numeric(19,4)</c> fails too — it would be absent from the query
    /// result rather than present with the wrong type.</para>
    /// </summary>
    [Fact]
    public async Task All22MoneyColumns_AreNumeric19And4InTheDatabase()
    {
        var actual = await ListAsync("""
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'finance'
              AND data_type = 'numeric'
              AND numeric_precision = 19
              AND numeric_scale = 4
            ORDER BY table_name, column_name;
            """);

        Assert.Equal(ExpectedMoneyColumns, actual);
    }

    /// <summary>
    /// A constraint that exists but does not bite reads as covered while enforcing nothing. One live
    /// rejection proves <c>ck_variations_client_diff</c> is armed — and proves the int rewrite landed on
    /// the right value, which a string-comparing constraint against an int column would not do: it would
    /// simply never match, never fire, and pass every existence check above.
    /// </summary>
    [Fact]
    public async Task TheClientDifferenceConstraint_ActuallyRejectsAVariationMissingItsAmounts()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("""
                INSERT INTO finance.variations
                    ("Id","Kind","ProjectId","Reason","Amount","Status","RequestedById","IsActive","CreatedAtUtc")
                VALUES (gen_random_uuid(), 1, gen_random_uuid(), 'no task, no amounts', 100, 1,
                        gen_random_uuid(), true, now() AT TIME ZONE 'utc');
                """));

        Assert.Equal("ck_variations_client_diff", ex.ConstraintName);
    }

    /// <summary>The other constraint, proven the same way: an internal change carrying signature fields
    /// is the confusion <c>Variation.Approve</c> refuses in the domain, and this is the database saying
    /// the same thing to any path that bypasses it.</summary>
    [Fact]
    public async Task TheInternalChangeConstraint_ActuallyRejectsSignatureFields()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("""
                INSERT INTO finance.variations
                    ("Id","Kind","ProjectId","BudgetLineId","Reason","Amount","Status","RequestedById",
                     "SignedDocumentPath","IsActive","CreatedAtUtc")
                VALUES (gen_random_uuid(), 2, gen_random_uuid(), gen_random_uuid(), 'signed internal', 100, 1,
                        gen_random_uuid(), '/signed.pdf', true, now() AT TIME ZONE 'utc');
                """));

        Assert.Equal("ck_variations_internal", ex.ConstraintName);
    }

    /// <summary>
    /// The complement of the two rejections above, and the reason they are not enough on their own: a
    /// constraint that rejected <i>everything</i> would pass both, and would also make the whole module
    /// unusable. This proves a legitimate row of each kind still inserts.
    ///
    /// <para>The internal-change row needs a real <c>budget_lines</c> row to point at, because
    /// <c>variations.BudgetLineId</c> carries an actual intra-schema foreign key. That is worth stating
    /// rather than working around: the check constraint says a variation of this kind must NAME a budget
    /// line, and the foreign key says the line it names must EXIST. Neither implies the other, and
    /// writing this test the lazy way — a random uuid — is what surfaced the difference.</para>
    /// </summary>
    [Fact]
    public async Task BothVariationKinds_StillInsertWhenTheyAreWellFormed()
    {
        await ExecuteAsync("""
            INSERT INTO finance.variations
                ("Id","Kind","ProjectId","ProjectTaskId","BudgetedAmount","ChosenAmount","Reason","Amount",
                 "Status","RequestedById","IsActive","CreatedAtUtc")
            VALUES (gen_random_uuid(), 1, gen_random_uuid(), gen_random_uuid(), 100, 150, 'client picked up',
                    50, 1, gen_random_uuid(), true, now() AT TIME ZONE 'utc');
            """);

        await ExecuteAsync("""
            INSERT INTO finance.budget_lines ("Id","Name","ProjectId","IsActive","CreatedAtUtc")
            VALUES ('00000000-0000-0000-0000-0000000000b1', 'target line', gen_random_uuid(), true,
                    now() AT TIME ZONE 'utc');
            """);

        await ExecuteAsync("""
            INSERT INTO finance.variations
                ("Id","Kind","ProjectId","BudgetLineId","Reason","Amount","Status","RequestedById",
                 "IsActive","CreatedAtUtc")
            VALUES (gen_random_uuid(), 2, gen_random_uuid(), '00000000-0000-0000-0000-0000000000b1',
                    'internal raise', 250, 1, gen_random_uuid(), true, now() AT TIME ZONE 'utc');
            """);

        Assert.Equal(2, await ScalarAsync("SELECT count(*) FROM finance.variations;"));
    }

    /// <summary>
    /// The four money columns on <c>budget_lines</c> default to zero, and the row inserted above supplied
    /// none of them. That default is not cosmetic: <c>ApplyCommitment</c> and <c>ApplyActualCost</c> move
    /// these columns by a delta, so a null would propagate through every subsequent posting. Asserted
    /// here rather than in the model tests because a default is a database fact.
    /// </summary>
    [Fact]
    public async Task ANewBudgetLine_StartsWithFourZeroedMoneyColumns()
    {
        await ExecuteAsync("""
            INSERT INTO finance.budget_lines ("Id","Name","ProjectId","IsActive","CreatedAtUtc")
            VALUES ('00000000-0000-0000-0000-0000000000d0', 'defaults probe', gen_random_uuid(), true,
                    now() AT TIME ZONE 'utc');
            """);

        var zeroed = await ScalarAsync("""
            SELECT count(*) FROM finance.budget_lines
            WHERE "Id" = '00000000-0000-0000-0000-0000000000d0'
              AND "AllocatedAmount" = 0 AND "AdjustmentAllocation" = 0
              AND "CommittedAmount" = 0 AND "ActualAmount" = 0;
            """);

        Assert.Equal(1, zeroed);
    }

    private Task<string[]> TablesAsync() => ListAsync("""
        SELECT table_name FROM information_schema.tables
        WHERE table_schema = 'finance' AND table_type = 'BASE TABLE'
        ORDER BY table_name;
        """);

    private Task<string[]> CheckConstraintsAsync() => ListAsync("""
        SELECT con.conname FROM pg_constraint con
        JOIN pg_namespace n ON n.oid = con.connamespace
        WHERE n.nspname = 'finance' AND con.contype = 'c'
        ORDER BY con.conname;
        """);

    private Task<string[]> IndexesAsync() => ListAsync("""
        SELECT indexname FROM pg_indexes WHERE schemaname = 'finance' ORDER BY indexname;
        """);

    private async Task<long> ScalarAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> ListAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection);
        await using var reader = await command.ExecuteReaderAsync();

        var results = new List<string>();
        while (await reader.ReadAsync()) results.Add(reader.GetString(0));
        return [.. results];
    }
}
