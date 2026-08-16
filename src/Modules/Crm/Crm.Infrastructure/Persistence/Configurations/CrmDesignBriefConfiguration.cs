using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmDesignBriefConfiguration : IEntityTypeConfiguration<CrmDesignBrief>
{
    public void Configure(EntityTypeBuilder<CrmDesignBrief> builder)
    {
        builder.ToTable("crm_design_briefs");
        builder.ConfigureEntityConventions();

        builder.Property(b => b.UnitType).HasConversion<int>();
        builder.Property(b => b.UnitCondition).HasConversion<int>();
        builder.Property(b => b.Address).HasMaxLength(512);
        builder.Property(b => b.BuildingKind).HasConversion<int?>();
        builder.Property(b => b.Position).HasConversion<int?>();
        builder.Property(b => b.ElevatorStatus).HasConversion<int?>();
        builder.Property(b => b.VisitorsNote).HasMaxLength(1000);
        builder.Property(b => b.DesignerExperience).HasConversion<int?>();
        builder.Property(b => b.DesignerExperienceReason).HasMaxLength(1000);
        builder.Property(b => b.HiringReasons).HasConversion<int>();
        builder.Property(b => b.PreferredStyle).HasMaxLength(256);
        builder.Property(b => b.PreferredColors).HasMaxLength(256);
        builder.Property(b => b.FurnitureStatus).HasConversion<int?>();
        builder.Property(b => b.TimelinePressure).HasConversion<int?>();
        builder.Property(b => b.ArchitecturalRequests).HasMaxLength(2000);
        builder.Property(b => b.OtherNeeds).HasMaxLength(2000);
        builder.Property(b => b.Sector).HasConversion<int?>();
        builder.Property(b => b.FinishingPurpose).HasConversion<int?>();
        builder.Property(b => b.Status).HasConversion<int>();

        // ProjectId: bare Guid, deliberately NO FK - crm precedes projects in SchemaOrder and
        // crm -> projects is not in rule 9's allow-list (see CrmMeetingConfiguration's identical note,
        // the canonical explanation for every ProjectId/ProjectStageId/ProjectTaskId column in this
        // module). One brief head per (project, stage) - the Application handler (G9-10) enforces the
        // uniqueness the backend expressed as a per-project unique index, generalized to per-stage for
        // this port's stage-scoped head (see the entity's own remarks).
        builder.HasIndex(b => b.ProjectId);
        builder.HasIndex(b => new { b.ProjectId, b.ProjectStageId }).IsUnique();

        // CreatedById: bare Guid FK to hr.employees(Id) - raw-SQL constraint added at G9-08.
        // LastMeetingId: no FK at all - same-schema but deliberately unvalidated (a stale reference is
        // harmless, purely informational).
        // A SiteId column was dropped at S1-00; the brief never pointed at a site. The real link is
        // Site.SourceBriefId, in the sites schema, pointing the other way - see the entity's remarks.
        builder.HasIndex(b => b.ProjectStageId).HasFilter("\"ProjectStageId\" IS NOT NULL");
        builder.HasIndex(b => b.CreatedById);
    }
}
