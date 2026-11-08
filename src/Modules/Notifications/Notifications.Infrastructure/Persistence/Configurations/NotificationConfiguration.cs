using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.RecipientUserId)
            .HasColumnName("RecipientUserId")
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(x => x.Type).HasConversion<int>().IsRequired();
        builder.Property(x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Body);

        builder.Property(x => x.EntityType).HasConversion<int>();
        builder.Property(x => x.EntityId);
        builder.Property(x => x.ProjectId); // deep-link hint; no FK (Projects' table) and no index (never filtered on)
        builder.Property(x => x.ReadAtUtc);

        builder.Property(x => x.DedupeKey).HasMaxLength(200);

        // The inbox read and the unread badge, which is the only query on the hot path.
        builder.HasIndex(x => new { x.RecipientUserId, x.ReadAtUtc })
            .HasDatabaseName("ix_notifications_recipient_read");

        // The idempotency guarantee the whole consolidated-sweep design rests on. FILTERED, because
        // notifications without a dedupe key (one-off, event-driven) are legitimately unlimited and
        // must not collide with each other on NULL. The key is already scoped per recipient by
        // Notification.ScopeDedupeKey, so this is "each user gets this reminder at most once", not
        // "only one user gets it".
        //
        // Declared through HasFilter rather than raw SQL, so the filter is part of the EF model and a
        // probe migration WOULD catch its removal. That is the difference from Sites' raw-SQL indexes,
        // which the probe is blind to; NotificationsSchemaObjectTests still names it, because the
        // uniqueness is load-bearing enough to be worth asserting against the live database too.
        builder.HasIndex(x => x.DedupeKey)
            .HasDatabaseName("ux_notifications_dedupe_key")
            .IsUnique()
            .HasFilter("\"DedupeKey\" IS NOT NULL");
    }
}
