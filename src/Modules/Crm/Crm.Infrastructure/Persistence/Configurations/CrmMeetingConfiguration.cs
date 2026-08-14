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

        builder.Property(m => m.Title).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Type).HasConversion<int>();
        builder.Property(m => m.Status).HasConversion<int>();
        builder.Property(m => m.Location).HasMaxLength(512);
        builder.Property(m => m.CancelReason).HasMaxLength(1000);

        // ProjectId: bare Guid FK to projects.projects(Id), no navigation - raw-SQL constraint at G9-08.
        builder.HasIndex(m => m.ProjectId);

        // PeriodId: same-schema, a real EF-managed FK, nullable.
        builder.HasOne<CrmRequirementPeriod>().WithMany().HasForeignKey(m => m.PeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.PeriodId).HasFilter("\"PeriodId\" IS NOT NULL");

        // RequestedByEmployeeId: bare Guid FK to hr.employees(Id) - raw-SQL constraint at G9-08.
        // RequestedByContactId: same-schema, a real EF-managed FK.
        builder.HasOne<CrmContact>().WithMany().HasForeignKey(m => m.RequestedByContactId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.Status);
    }
}
