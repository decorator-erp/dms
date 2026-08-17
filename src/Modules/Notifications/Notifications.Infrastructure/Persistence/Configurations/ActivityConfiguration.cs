using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activities");
        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.ActorUserId).HasMaxLength(450);
        builder.Property(x => x.ActorName).HasMaxLength(256);

        builder.Property(x => x.Action).HasConversion<int>().IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(256);
        builder.Property(x => x.EntityDisplayName).HasMaxLength(256);

        builder.Property(x => x.ProjectId);
        builder.Property(x => x.Message).IsRequired();
        builder.Property(x => x.OccurredAtUtc).IsRequired();

        // The per-entity timeline read.
        builder.HasIndex(x => new { x.EntityName, x.EntityId, x.OccurredAtUtc })
            .HasDatabaseName("ix_activities_entity")
            .IsDescending(false, false, true);

        // The project timeline read. Filtered, because ProjectId is null for every activity that is
        // not project-scoped and indexing those rows serves no query.
        builder.HasIndex(x => new { x.ProjectId, x.OccurredAtUtc })
            .HasDatabaseName("ix_activities_project")
            .IsDescending(false, true)
            .HasFilter("\"ProjectId\" IS NOT NULL");
    }
}
