using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmMeetingSlotConfiguration : IEntityTypeConfiguration<CrmMeetingSlot>
{
    public void Configure(EntityTypeBuilder<CrmMeetingSlot> builder)
    {
        builder.ToTable("crm_meeting_slots");
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Status).HasConversion<int>();

        builder.HasOne<CrmMeeting>().WithMany().HasForeignKey(s => s.MeetingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.MeetingId);

        // ProposedByContactId: same-schema, a real EF-managed FK. ProposedByEmployeeId: bare Guid FK to
        // hr.employees(Id) - raw-SQL constraint at G9-08.
        builder.HasOne<CrmContact>().WithMany().HasForeignKey(s => s.ProposedByContactId).OnDelete(DeleteBehavior.Restrict);

        // At most one Accepted slot per meeting - partial unique index, mirroring the backend.
        builder.HasIndex(s => s.MeetingId)
            .HasDatabaseName("ux_crm_meeting_slots_meeting_accepted")
            .IsUnique()
            .HasFilter("\"Status\" = 2");
    }
}
