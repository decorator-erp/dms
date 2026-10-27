using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmRequirementItemConfiguration : IEntityTypeConfiguration<CrmRequirementItem>
{
    public void Configure(EntityTypeBuilder<CrmRequirementItem> builder)
    {
        builder.ToTable("crm_requirement_items");
        builder.ConfigureEntityConventions();

        builder.Property(i => i.Category).HasConversion<int>();
        builder.Property(i => i.Room).HasMaxLength(128);
        builder.Property(i => i.Title).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(2000);
        builder.Property(i => i.Priority).HasConversion<int>();
        builder.Property(i => i.SourceType).HasConversion<int?>();
        builder.Property(i => i.ApprovalStatus).HasConversion<int>();

        // ProjectId/ProjectTaskId: bare Guids, deliberately NO FK - see CrmMeetingConfiguration's note
        // (crm precedes projects in SchemaOrder; crm -> projects is not in rule 9's allow-list).
        // CreatedByEmployeeId (the plan's "EmployeeId"): bare Guid FK to hr.employees(Id) - raw-SQL
        // constraint added at G9-08.
        builder.HasIndex(i => i.ProjectId);

        builder.HasOne<CrmRequirementPeriod>().WithMany().HasForeignKey(i => i.PeriodId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => i.PeriodId);

        builder.HasOne<CrmSpaceType>().WithMany().HasForeignKey(i => i.SpaceTypeId).OnDelete(DeleteBehavior.Restrict);

        // MeetingId/BriefId/NoteId: same-schema, unvalidated (source references are informational
        // only, mirroring CrmDesignBrief.LastMeetingId's own reasoning) - no EF-managed FK, so an
        // orphaned reference after a hard delete elsewhere can never block this row's own lifecycle.
        builder.HasOne<CrmRequirementItem>().WithMany().HasForeignKey(i => i.SupersedesItemId).OnDelete(DeleteBehavior.Restrict);

        // A-6 (B-M3): an item is superseded at most once, so the correction ledger cannot fork. Unique
        // where set; ItemWrites maps the violation to crm.items.AlreadySuperseded.
        builder.HasIndex(i => i.SupersedesItemId).IsUnique().HasFilter("\"SupersedesItemId\" IS NOT NULL");

        builder.HasOne<CrmContact>().WithMany().HasForeignKey(i => i.CreatedByContactId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.ApprovalStatus);
        builder.HasIndex(i => i.CreatedByEmployeeId).HasFilter("\"CreatedByEmployeeId\" IS NOT NULL");
    }
}
