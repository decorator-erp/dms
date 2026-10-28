using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmMeetingConfiguration : IEntityTypeConfiguration<CrmMeeting>
{
    public void Configure(EntityTypeBuilder<CrmMeeting> builder)
    {
        builder.ToTable("crm_meetings");
        builder.ConfigureEntityConventions();

        // B-8 (M4): concurrency token; MeetingWrites maps a conflict to crm.meetings.ConcurrentUpdate.
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        builder.Property(m => m.Title).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Type).HasConversion<int>();
        builder.Property(m => m.Status).HasConversion<int>();
        builder.Property(m => m.Location).HasMaxLength(512);
        builder.Property(m => m.CancelReason).HasMaxLength(1000);

        // ProjectId: bare Guid, deliberately NO FK - crm precedes projects in SchemaOrder (structural
        // item 1), so projects.projects doesn't exist yet when this migration runs, and crm -> projects
        // is not in rule 9's allow-list regardless (only the reverse, projects -> crm, is - see
        // docs/cross-schema-foreign-keys.md's CRM entry). Same treatment as every other CRM ProjectId/
        // ProjectStageId/ProjectTaskId column in this module.
        builder.HasIndex(m => m.ProjectId);

        // PeriodId: same-schema, a real EF-managed FK, nullable.
        builder.HasOne<CrmRequirementPeriod>().WithMany().HasForeignKey(m => m.PeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.PeriodId).HasFilter("\"PeriodId\" IS NOT NULL");

        // RequestedByEmployeeId: bare Guid FK to hr.employees(Id) - raw-SQL constraint added at G9-08
        // (crm -> hr IS in rule 9's allow-list, and hr precedes crm in SchemaOrder).
        // RequestedByContactId: same-schema, a real EF-managed FK.
        builder.HasIndex(m => m.RequestedByEmployeeId).HasFilter("\"RequestedByEmployeeId\" IS NOT NULL");
        builder.HasOne<CrmContact>().WithMany().HasForeignKey(m => m.RequestedByContactId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.Status);
    }
}
