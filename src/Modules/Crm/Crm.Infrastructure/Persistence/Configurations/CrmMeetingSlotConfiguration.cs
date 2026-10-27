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

        // B-14 (MT-N5): both MeetingId indexes are named. Two unnamed HasIndex calls on the same column are
        // one index to EF, so the plain one used to be replaced by the partial unique one below.
        builder.HasIndex(s => s.MeetingId, "ix_crm_meeting_slots_meeting_id");

        // ProposedByContactId: same-schema, a real EF-managed FK. ProposedByEmployeeId: bare Guid FK to
        // hr.employees(Id) - raw-SQL constraint added at G9-08.
        builder.HasIndex(s => s.ProposedByEmployeeId).HasFilter("\"ProposedByEmployeeId\" IS NOT NULL");
        builder.HasOne<CrmContact>().WithMany().HasForeignKey(s => s.ProposedByContactId).OnDelete(DeleteBehavior.Restrict);

        // At most one Accepted slot per meeting - partial unique index, mirroring the backend.
        builder.HasIndex(s => s.MeetingId, "ux_crm_meeting_slots_meeting_accepted")
            .IsUnique()
            .HasFilter("\"Status\" = 2");
    }
}
