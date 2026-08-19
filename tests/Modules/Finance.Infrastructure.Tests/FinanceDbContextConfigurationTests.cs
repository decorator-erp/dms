using System.Reflection;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finance.Infrastructure.Tests;

/// <summary>
/// Settles the entity/configuration count by counting, not by trusting a plan number — the G9-07
/// precedent, which caught a missing EF configuration in CRM.
///
/// <para><b>Stronger than that precedent in one respect.</b> Sites and CRM compare each count against a
/// shared <c>EntityCount</c> constant; the edit that drops a configuration and updates the constant in
/// the same pass still passes. Here the two counts are compared <i>to each other</i>, with no literal
/// standing between them, so dropping a configuration fails no matter what else is edited alongside it.
/// The named-list test then pins which nine entities they are, because two equal counts can still
/// describe different sets.</para>
///
/// <para>No Postgres needed: EF builds its model from the compiled configuration graph, never from a
/// live connection, so this runs without Docker. The live-database half — what Postgres actually
/// created — is <c>FinanceSchemaObjectTests</c>.</para>
/// </summary>
public class FinanceDbContextConfigurationTests
{
    private static readonly string[] ExpectedEntities =
    [
        "BoqSnapshot", "BoqSnapshotLine", "BudgetLine", "CostCategory", "Invoice",
        "InvoiceLine", "LedgerAllocation", "LedgerEntry", "Variation",
    ];

    private static FinanceDbContext CreateContext()
    {
        // Syntactically valid but never connected to -- building the model opens no connection.
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        return new FinanceDbContext(options);
    }

    private static int DbSetPropertyCount() =>
        typeof(FinanceDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

    private static int ConfigurationCount() =>
        typeof(FinanceDbContext).Assembly.GetTypes()
            .Count(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));

    [Fact]
    public void EveryDbSet_HasExactlyOneEntityTypeConfiguration()
    {
        var dbSets = DbSetPropertyCount();
        var configurations = ConfigurationCount();

        // Liveness. Both sides are reflection results, and both go to zero the same way — a renamed
        // assembly, a moved namespace, a DbSet expressed some other way. Zero equals zero, so the
        // comparison below would agree that nothing matches nothing.
        Assert.True(dbSets > 0, "Found no DbSet properties on FinanceDbContext; the reflection is broken.");
        Assert.True(configurations > 0, "Found no IEntityTypeConfiguration types; the reflection is broken.");

        Assert.True(
            dbSets == configurations,
            $"FinanceDbContext exposes {dbSets} DbSet properties but Finance.Infrastructure declares "
            + $"{configurations} entity configurations. An entity without a configuration is mapped by "
            + "convention -- wrong table name, wrong schema, no precision on its money columns -- and "
            + "nothing else in the suite would say so.");
    }

    /// <summary>
    /// Two equal counts can still describe different sets: nine configurations for eight entities plus
    /// one stray passes the test above. This pins the actual list.
    /// </summary>
    /// <summary>Mapped by <c>ConfigureOutbox</c>/<c>ConfigureAudit</c> rather than by this module, and
    /// owned by the <c>shared</c> schema. There are three, not two — <c>ConfigureOutbox</c> maps
    /// <c>InboxMessage</c> alongside <c>OutboxMessage</c> so the inbox dedupe decorator can resolve
    /// <c>Set&lt;InboxMessage&gt;()</c> on the module context.</summary>
    private static readonly string[] SharedSchemaEntities = ["AuditLog", "InboxMessage", "OutboxMessage"];

    [Fact]
    public void Model_ContainsExactlyTheNineFinanceEntities()
    {
        using var context = CreateContext();

        var entityNames = context.Model.GetEntityTypes()
            .Select(e => e.ClrType.Name)
            .Where(n => !SharedSchemaEntities.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedEntities, entityNames);
    }

    /// <summary>
    /// The shared tables are mapped but not owned — <c>ownsMigration: false</c> — so they must appear in
    /// the model (or the audit interceptor throws on the first auditable write) while belonging to
    /// <c>shared</c>, not to <c>finance</c>.
    ///
    /// <para>Asserted as an exact set rather than a subset: the test above subtracts these three names,
    /// so a shared entity that stopped being mapped would silently shrink what that test checks instead
    /// of failing anywhere.</para>
    /// </summary>
    [Fact]
    public void TheSharedTables_AreMappedButLiveInTheSharedSchema()
    {
        using var context = CreateContext();

        var shared = context.Model.GetEntityTypes()
            .Where(e => SharedSchemaEntities.Contains(e.ClrType.Name))
            .ToArray();

        Assert.Equal(SharedSchemaEntities.Length, shared.Length);

        foreach (var entity in shared)
            Assert.Equal("shared", entity.GetSchema());
    }

    [Fact]
    public void EveryFinanceTable_LivesInTheFinanceSchema()
    {
        using var context = CreateContext();

        var financeEntities = context.Model.GetEntityTypes()
            .Where(e => ExpectedEntities.Contains(e.ClrType.Name))
            .ToArray();

        // Liveness: an empty match set would make the loop below assert nothing at all.
        Assert.Equal(ExpectedEntities.Length, financeEntities.Length);

        foreach (var entity in financeEntities)
            Assert.Equal("finance", entity.GetSchema());
    }

    /// <summary>
    /// <b>Every decimal property in the model carries an explicit <c>numeric(19,4)</c>.</b>
    ///
    /// <para>This is written as "no decimal property lacks it" rather than as a count, so a money column
    /// added later is covered the day it is added rather than the day someone remembers to extend a
    /// list. The count is asserted separately, below, because "none lacks it" is vacuously true of an
    /// empty set.</para>
    ///
    /// <para>Precision is not decoration here. <c>InvoiceLine.Create</c> rounds its product to 4 places
    /// specifically to match this column type; a column that quietly became <c>numeric(19,2)</c> would
    /// truncate the rounded value again, on the database side, where no unit test is looking.</para>
    /// </summary>
    [Fact]
    public void EveryMoneyColumn_IsExplicitlyNumeric19And4()
    {
        using var context = CreateContext();

        var decimalProperties = context.Model.GetEntityTypes()
            .Where(e => ExpectedEntities.Contains(e.ClrType.Name))
            .SelectMany(e => e.GetProperties()
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
                .Select(p => (Table: e.GetTableName(), Column: p.Name, Type: p.GetColumnType())))
            .ToArray();

        // Liveness, and a recount rather than a trusted number: 22 money columns across 8 of the 9
        // tables (cost_categories has none). Written as an equality, not a floor, so a decimal column
        // deleted by an unrelated edit is as visible as one added without precision.
        Assert.Equal(22, decimalProperties.Length);

        var wrong = decimalProperties
            .Where(p => p.Type != "numeric(19,4)")
            .Select(p => $"{p.Table}.{p.Column} is {p.Type ?? "convention-typed"}")
            .ToArray();

        Assert.True(wrong.Length == 0,
            "These decimal columns are not numeric(19,4): " + string.Join(", ", wrong));
    }
}
