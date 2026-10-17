using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Xunit;

namespace Notifications.Infrastructure.Tests;

/// <summary>
/// The DbSet-count / configuration-count guard, in the shape <c>CrmDbContextConfigurationTests</c>
/// (G9-07) established and <c>SitesDbContextConfigurationTests</c> repeated: an entity added to the
/// context without a matching <c>IEntityTypeConfiguration</c> gets EF's conventions — a PascalCase
/// table in the right schema, and none of the indexes or lengths anyone intended — and nothing else
/// notices.
///
/// <para>No database needed: every assertion here is about the model, not about what was applied.</para>
/// </summary>
public class NotificationsDbContextConfigurationTests
{
    private const int EntityCount = 2;

    private static NotificationsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=x;Password=y")
            .Options);

    [Fact]
    public void NotificationsDbContext_HasExactlyTwoDbSetProperties()
    {
        int dbSets = typeof(NotificationsDbContext)
            .GetProperties()
            .Count(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Equal(EntityCount, dbSets);
    }

    [Fact]
    public void Assembly_HasExactlyTwoEntityTypeConfigurations()
    {
        int configurations = typeof(NotificationsDbContext).Assembly
            .GetTypes()
            .Count(t => !t.IsAbstract && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));

        Assert.Equal(EntityCount, configurations);
    }

    /// <summary>
    /// The model holds exactly the two owned entities plus the three <c>shared</c> tables this context
    /// maps but does not own. Pinning the list by name is what makes an accidental addition visible —
    /// a count alone would be satisfied by any swap.
    /// </summary>
    [Fact]
    public void Model_ContainsTheTwoNotifyEntities_PlusTheThreeMappedSharedTables()
    {
        using NotificationsDbContext context = CreateContext();

        var entityNames = context.Model.GetEntityTypes()
            .Select(e => e.ClrType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                nameof(Activity),
                nameof(AuditLog),
                nameof(InboxMessage),
                nameof(Notification),
                nameof(OutboxMessage),
            ],
            entityNames);
    }

    /// <summary>
    /// Both owned tables are in <c>notify</c>; the three borrowed ones are in <c>shared</c> and stay
    /// there. This is the assertion that would catch <c>ConfigureAudit</c>/<c>ConfigureOutbox</c> being
    /// called without their schema, which would quietly create a second copy of each in <c>notify</c>.
    /// </summary>
    [Fact]
    public void OwnedTablesLiveInNotify_AndTheBorrowedOnesStayInShared()
    {
        using NotificationsDbContext context = CreateContext();

        var schemas = context.Model.GetEntityTypes()
            .ToDictionary(e => e.ClrType.Name, e => e.GetSchema());

        Assert.Equal("notify", schemas[nameof(Notification)]);
        Assert.Equal("notify", schemas[nameof(Activity)]);

        Assert.Equal("shared", schemas[nameof(AuditLog)]);
        Assert.Equal("shared", schemas[nameof(OutboxMessage)]);
        Assert.Equal("shared", schemas[nameof(InboxMessage)]);
    }

    /// <summary>
    /// The three <c>shared</c> tables are mapped for reading and writing but excluded from this
    /// module's migration — the <c>ExcludeFromMigrations</c> split. Without it, two modules' migrations
    /// would each try to create <c>shared.outbox_messages</c> and the second would fail on a fresh
    /// database.
    /// </summary>
    [Fact]
    public void TheThreeSharedTables_AreExcludedFromThisModulesMigration()
    {
        using NotificationsDbContext context = CreateContext();

        // The design-time model, not context.Model: ExcludeFromMigrations is a migration-time concern
        // and EF strips it from the runtime read-optimized model, which throws if asked.
        IModel model = context.GetService<IDesignTimeModel>().Model;

        foreach (string? name in new[] { nameof(AuditLog), nameof(OutboxMessage), nameof(InboxMessage) })
        {
            IEntityType entity = model.GetEntityTypes().Single(e => e.ClrType.Name == name);
            Assert.True(entity.IsTableExcludedFromMigrations(),
                $"{name} must be excluded from the notify migration - shared owns it.");
        }
    }
}
