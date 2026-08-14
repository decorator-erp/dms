using System.Reflection;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Crm.Infrastructure.Tests;

/// <summary>
/// Settles the entity/configuration count by counting, not by trusting either plan's number — see
/// this task's PROGRESS.md entry. A mismatch here is a missing (or extra) EF configuration, which is
/// exactly the bug an assumed number would hide. No Postgres/Testcontainers needed: EF's model is
/// built lazily from the compiled configuration graph, never from a live connection, so this runs
/// without Docker.
/// </summary>
public class CrmDbContextConfigurationTests
{
    private static CrmDbContext CreateContext()
    {
        // A syntactically valid but never-connected-to connection string - building the EF model
        // (Model, EntityTypes) never opens a connection; only executing a query would.
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        return new CrmDbContext(options);
    }

    [Fact]
    public void CrmDbContext_HasExactly24DbSetProperties()
    {
        var dbSetPropertyCount = typeof(CrmDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Equal(24, dbSetPropertyCount);
    }

    [Fact]
    public void Assembly_HasExactly24EntityTypeConfigurations()
    {
        var configurationCount = typeof(CrmDbContext).Assembly.GetTypes()
            .Count(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));

        Assert.Equal(24, configurationCount);
    }

    [Fact]
    public void EveryDbSetEntityType_HasExactlyOneConfiguration()
    {
        // The two counts being equal (previous two facts) isn't sufficient on its own - two DbSets
        // could each lack a configuration while two OTHER types have duplicate/stray ones and the
        // totals would still match. This cross-checks by entity TYPE, not just by count.
        var dbSetEntityTypes = typeof(CrmDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToList();

        var configuredEntityTypes = typeof(CrmDbContext).Assembly.GetTypes()
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
                .Select(i => i.GetGenericArguments()[0]))
            .ToList();

        Assert.Equal(dbSetEntityTypes.OrderBy(t => t.Name), configuredEntityTypes.OrderBy(t => t.Name));
    }

    [Fact]
    public void Model_UsesCrmSchema()
    {
        using var context = CreateContext();

        var entityTypes = context.Model.GetEntityTypes().ToList();

        Assert.NotEmpty(entityTypes);
        // The outbox message type is shared infrastructure configured by ConfigureOutbox and doesn't
        // carry the module's own default schema the same way - exclude it and assert every CRM entity
        // type explicitly lands in "crm".
        var crmEntityTypes = entityTypes.Where(e => e.ClrType.Namespace == "Crm.Domain").ToList();
        Assert.NotEmpty(crmEntityTypes);
        Assert.All(crmEntityTypes, e => Assert.Equal("crm", e.GetSchema()));
    }
}
