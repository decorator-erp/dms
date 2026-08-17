using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>notify</c> schema: <c>notifications</c> and <c>activities</c>.
///
/// <para><b>It does not own <c>audit_logs</c>, despite serving the audit endpoints.</b> That table
/// lives in <c>shared</c> and is owned by <c>SharedDbContext</c> (N1-02) — mapped here with
/// <c>ExcludeFromMigrations</c>, exactly as every module maps the outbox tables it reads but does not
/// create. Reading a <c>shared</c> table through a module context is the established shape, not a
/// compromise: <c>OutboxProcessor</c> already does it from a background scope.</para>
/// </summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Activity> Activities => Set<Activity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notify");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
