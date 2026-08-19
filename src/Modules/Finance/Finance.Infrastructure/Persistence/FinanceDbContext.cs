using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

/// <summary>
/// 9 <see cref="DbSet{TEntity}"/> properties — one per entity in <c>Finance.Domain</c>, each with its
/// own EF configuration. Nothing here is JSON-owned and nothing is a static rule class, so unlike CRM
/// there is no entity deliberately without a <c>DbSet</c>.
///
/// <para><b>The count is asserted, never assumed.</b>
/// <c>Finance.Infrastructure.Tests.FinanceDbContextConfigurationTests</c> compares the configuration
/// count against the <c>DbSet</c> count <i>directly</i> — neither is written down as a literal — and
/// then pins the model's actual entity list by name. The G9-07 precedent (which caught a missing EF
/// configuration in CRM) used a shared constant; comparing the two counts to each other is strictly
/// stronger, because a constant updated in the same edit that drops a configuration still passes.</para>
///
/// <para><b>Columns stay PascalCase and enums are stored as <c>int</c>.</b> Both are repo conventions
/// that diverge from the backend, deliberately:
/// <list type="bullet">
/// <item>The backend's finance configurations map every column to an explicit snake_case name. That
/// mapping is not ported (the established rule since G0-08), so table names are snake_case only
/// because <c>ToTable</c> says so, and every hand-written SQL statement must quote its column names in
/// PascalCase or Postgres folds the unquoted identifier to lowercase.</item>
/// <item>The backend stores every finance enum as a string (<c>HasConversion&lt;string&gt;()</c>). This
/// repo stores enums as <c>int</c> in all eight existing modules, and
/// <c>Finance.UnitTests.EnumParityTests</c> exists precisely because the numeric values are what land
/// in the column. Storing text here would make those parity tests decorative and Finance the only
/// module whose enum columns cannot be range-checked. The two <c>variations</c> check constraints the
/// backend writes against string literals are therefore rewritten against ints — see
/// <c>VariationConfiguration</c>.</item>
/// </list></para>
///
/// <para><b>Money is <c>numeric(19,4)</c> on all 22 decimal columns.</b> The backend declares
/// <c>decimal(19,4)</c> — the same Postgres type under the name EF uses for SQL Server — on every one
/// of them, with zero <c>HasPrecision</c> calls anywhere. <c>numeric(19,4)</c> is this repo's spelling
/// (Sites uses <c>numeric(18,4)</c>), and preserves precision and scale exactly. The scale matters
/// beyond storage: <c>InvoiceLine.Create</c> rounds to 4 places to match it.</para>
/// </summary>
internal sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    // Budgeting
    public DbSet<CostCategory> CostCategories => Set<CostCategory>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();

    // Ledger
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<LedgerAllocation> LedgerAllocations => Set<LedgerAllocation>();

    // Invoicing
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    // Change control
    public DbSet<Variation> Variations => Set<Variation>();

    // Reporting snapshots
    public DbSet<BoqSnapshot> BoqSnapshots => Set<BoqSnapshot>();
    public DbSet<BoqSnapshotLine> BoqSnapshotLines => Set<BoqSnapshotLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("finance");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
