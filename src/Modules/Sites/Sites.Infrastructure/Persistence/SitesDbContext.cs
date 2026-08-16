using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Sites.Domain;
using Sites.Domain.Definition;

namespace Sites.Infrastructure.Persistence;

/// <summary>
/// 8 <see cref="DbSet{TEntity}"/> properties — one per entity with its own EF configuration, and every
/// entity in <c>Sites.Domain</c> has one. Unlike CRM there is no entity deliberately without a
/// <c>DbSet</c>: nothing here is JSON-owned and nothing is a static rule class.
///
/// <para>The count is asserted against the assembly's configuration count by
/// <c>Sites.Infrastructure.Tests.SitesDbContextConfigurationTests</c> rather than assumed — the G9-07
/// precedent, which caught a missing configuration in CRM.</para>
///
/// <para><b>Columns stay PascalCase.</b> This solution configures no snake_case naming convention (the
/// backend's own configurations map every column explicitly, and that mapping is deliberately NOT
/// ported); table names are snake_case only because <c>ToTable</c> says so. Every hand-written SQL
/// statement in this module — check constraints, functional indexes, cross-schema foreign keys — must
/// therefore quote its column names in PascalCase, or Postgres folds the unquoted identifier to
/// lowercase and silently references a column that does not exist.</para>
/// </summary>
internal sealed class SitesDbContext(DbContextOptions<SitesDbContext> options) : DbContext(options)
{
    // Instances
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<SiteRecord> SiteRecords => Set<SiteRecord>();
    public DbSet<SiteRecordValue> SiteRecordValues => Set<SiteRecordValue>();
    public DbSet<SiteDocument> SiteDocuments => Set<SiteDocument>();

    // Definition (global master data)
    public DbSet<SiteModule> SiteModules => Set<SiteModule>();
    public DbSet<SiteItem> SiteItems => Set<SiteItem>();
    public DbSet<SiteItemOption> SiteItemOptions => Set<SiteItemOption>();
    public DbSet<SiteModuleItem> SiteModuleItems => Set<SiteModuleItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sites");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SitesDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
    }
}
