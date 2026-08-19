using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sites.Infrastructure.Persistence;
using Xunit;

namespace Sites.Infrastructure.Tests;

/// <summary>
/// Settles the entity/configuration count by counting, not by trusting a plan's number — the G9-07
/// precedent, which caught a missing EF configuration in CRM. A mismatch here is exactly the bug an
/// assumed number would hide.
///
/// <para>No Postgres needed: EF builds its model from the compiled configuration graph, never from a
/// live connection, so this runs without Docker.</para>
/// </summary>
public class SitesDbContextConfigurationTests
{
    private const int EntityCount = 8;

    private static SitesDbContext CreateContext()
    {
        // Syntactically valid but never connected to -- building the model opens no connection.
        var options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        return new SitesDbContext(options);
    }

    [Fact]
    public void SitesDbContext_HasExactlyEightDbSetProperties()
    {
        var dbSetPropertyCount = typeof(SitesDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Equal(EntityCount, dbSetPropertyCount);
    }

    [Fact]
    public void Assembly_HasExactlyEightEntityTypeConfigurations()
    {
        var configurationCount = typeof(SitesDbContext).Assembly.GetTypes()
            .Count(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));

        Assert.Equal(EntityCount, configurationCount);
    }

    /// <summary>
    /// The two counts above can agree while still describing different sets — eight configurations for
    /// seven entities plus one stray would pass both. This pins the model's actual entity list, which is
    /// what a missing configuration would change.
    /// </summary>
    [Fact]
    public void Model_ContainsExactlyTheEightSiteEntities()
    {
        using var context = CreateContext();

        var entityNames = context.Model.GetEntityTypes()
            .Select(e => e.ClrType.Name)
            .Where(n => n.StartsWith("Site", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Site", "SiteDocument", "SiteItem", "SiteItemOption",
                "SiteModule", "SiteModuleItem", "SiteRecord", "SiteRecordValue",
            ],
            entityNames);
    }

    [Fact]
    public void EverySiteTableLivesInTheSitesSchema()
    {
        using var context = CreateContext();

        var siteEntities = context.Model.GetEntityTypes()
            .Where(e => e.ClrType.Name.StartsWith("Site", StringComparison.Ordinal))
            .ToArray();

        // PC-03 liveness: "no Site entity sits outside the sites schema" is trivially true when the
        // filter matches no Site entity at all — a renamed type prefix or an unbuilt model both read as
        // compliance. The sibling test pins the exact eight; this one only needs to know it found them.
        Assert.Equal(EntityCount, siteEntities.Length);

        var strays = siteEntities
            .Where(e => e.GetSchema() is not null and not "sites")
            .Select(e => $"{e.ClrType.Name} -> {e.GetSchema()}")
            .ToArray();

        Assert.Empty(strays);
    }

    /// <summary>
    /// <c>SiteRecordValue</c> is the module's one non-<c>Entity</c>, and its composite key is the whole
    /// reason. Asserted because a well-meaning "add an Id like everything else" refactor would compile,
    /// migrate, and silently permit two values for one (record, item) pair.
    /// </summary>
    [Fact]
    public void SiteRecordValue_IsKeyedByTheRecordItemPair()
    {
        using var context = CreateContext();

        var key = context.Model.FindEntityType(typeof(Domain.SiteRecordValue))!.FindPrimaryKey()!;

        Assert.Equal(["RecordId", "ItemId"], key.Properties.Select(p => p.Name));
        Assert.Equal("pk_site_record_values", key.GetName());
    }
}
