using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Owns the <c>shared</c> schema: <c>outbox_messages</c> and <c>inbox_messages</c>, and nothing else.
/// Every module's own DbContext also maps both tables (see <see cref="OutboxModelConfiguration"/>) but
/// with <c>ExcludeFromMigrations()</c> — this is the only context whose migration actually creates them.
/// </summary>
public sealed class SharedDbContext(DbContextOptions<SharedDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shared");
        modelBuilder.ConfigureOutbox(ownsMigration: true);
    }
}
