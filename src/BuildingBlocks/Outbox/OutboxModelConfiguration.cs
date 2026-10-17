using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Maps <c>shared.outbox_messages</c> and <c>shared.inbox_messages</c>. Call with
/// <paramref name="ownsMigration"/> = true from exactly one place — <see cref="SharedDbContext"/>,
/// which owns the migration for both tables — and false from every module's own DbContext.
///
/// Every module maps both tables because <see cref="DbContextOutboxExtensions.Outbox"/> and
/// <see cref="InboxDedupeDecorator{TContext,TEvent}"/> need <c>Set&lt;OutboxMessage&gt;()</c> /
/// <c>Set&lt;InboxMessage&gt;()</c> to resolve on the module's OWN context — that is what lets the
/// inbox row commit in the same <c>SaveChangesAsync</c> call as the module's own write, instead of a
/// second, separately-committed write through <see cref="SharedDbContext"/> that a crash between the
/// two could leave inconsistent.
/// </summary>
public static class OutboxModelConfiguration
{
    public static void ConfigureOutbox(this ModelBuilder modelBuilder, bool ownsMigration)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", "shared", t =>
            {
                if (!ownsMigration)
                {
                    t.ExcludeFromMigrations();
                }
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Type).HasColumnName("type").IsRequired();
            b.Property(x => x.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.OccurredUtc).HasColumnName("occurred_utc").IsRequired();
            b.Property(x => x.ProcessedUtc).HasColumnName("processed_utc");
            b.Property(x => x.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            b.Property(x => x.Error).HasColumnName("error");
            b.HasIndex(x => x.OccurredUtc)
                .HasDatabaseName("ix_outbox_messages_unprocessed")
                .HasFilter("processed_utc IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages", "shared", t =>
            {
                if (!ownsMigration)
                {
                    t.ExcludeFromMigrations();
                }
            });
            b.HasKey(x => new { x.EventId, x.HandlerName });
            b.Property(x => x.EventId).HasColumnName("event_id");
            b.Property(x => x.HandlerName).HasColumnName("handler_name").HasMaxLength(256);
            b.Property(x => x.ProcessedUtc).HasColumnName("processed_utc").IsRequired();
        });
    }
}
