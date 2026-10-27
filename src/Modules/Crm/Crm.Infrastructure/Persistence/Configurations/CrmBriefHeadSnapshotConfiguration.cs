using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmBriefHeadSnapshotConfiguration : IEntityTypeConfiguration<CrmBriefHeadSnapshot>
{
    public void Configure(EntityTypeBuilder<CrmBriefHeadSnapshot> builder)
    {
        builder.ToTable("crm_brief_head_snapshots");
        builder.Property(s => s.Id).ValueGeneratedNever();
        // Not IAuditable, matching the entity's own base type (Entity only) - immutable once written,
        // there is nothing to audit an update against.

        builder.Property(s => s.UnitType).HasConversion<int>();
        builder.Property(s => s.UnitCondition).HasConversion<int>();
        builder.Property(s => s.Address).HasMaxLength(512);
        builder.Property(s => s.BuildingKind).HasConversion<int?>();
        builder.Property(s => s.Position).HasConversion<int?>();
        builder.Property(s => s.ElevatorStatus).HasConversion<int?>();
        builder.Property(s => s.VisitorsNote).HasMaxLength(1000);
        builder.Property(s => s.DesignerExperience).HasConversion<int?>();
        builder.Property(s => s.DesignerExperienceReason).HasMaxLength(1000);
        builder.Property(s => s.HiringReasons).HasConversion<int>();
        builder.Property(s => s.PreferredStyle).HasMaxLength(256);
        builder.Property(s => s.PreferredColors).HasMaxLength(256);
        builder.Property(s => s.FurnitureStatus).HasConversion<int?>();
        builder.Property(s => s.TimelinePressure).HasConversion<int?>();
        builder.Property(s => s.ArchitecturalRequests).HasMaxLength(2000);
        builder.Property(s => s.OtherNeeds).HasMaxLength(2000);
        builder.Property(s => s.Sector).HasConversion<int?>();
        builder.Property(s => s.Outcome).HasConversion<int?>();
        builder.Property(s => s.FinishingPurpose).HasConversion<int?>();

        builder.HasOne<CrmDesignBrief>().WithMany().HasForeignKey(s => s.BriefId).OnDelete(DeleteBehavior.Cascade);

        // One row per (BriefId, Round); only Outcome/DecidedAtUtc are written after creation, once.
        builder.HasIndex(s => new { s.BriefId, s.Round }).IsUnique();

        // ProjectId: bare Guid, deliberately NO FK - see CrmMeetingConfiguration's note (crm precedes
        // projects in SchemaOrder; crm -> projects is not in rule 9's allow-list).
    }
}
