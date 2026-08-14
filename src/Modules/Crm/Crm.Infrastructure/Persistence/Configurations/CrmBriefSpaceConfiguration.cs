using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmBriefSpaceConfiguration : IEntityTypeConfiguration<CrmBriefSpace>
{
    public void Configure(EntityTypeBuilder<CrmBriefSpace> builder)
    {
        builder.ToTable("crm_brief_spaces");
        builder.ConfigureEntityConventions();

        builder.Property(e => e.Requirements).HasMaxLength(2000);
        builder.Property(e => e.ApprovalStatus).HasConversion<int>();

        builder.HasOne<CrmDesignBrief>().WithMany().HasForeignKey(e => e.BriefId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.BriefId);

        builder.HasOne<CrmSpace>().WithMany().HasForeignKey(e => e.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.SpaceId).HasFilter("\"SpaceId\" IS NOT NULL");

        // Self-reference: the ledger's supersession chain.
        builder.HasOne<CrmBriefSpace>().WithMany().HasForeignKey(e => e.SupersedesSpaceId).OnDelete(DeleteBehavior.Restrict);

        // MeetingId: same-schema, unvalidated (informational source reference, same reasoning as
        // CrmDesignBrief.LastMeetingId). ProjectTaskId: bare Guid FK to projects.projects (task table),
        // raw-SQL constraint at G9-08. CreatedById: bare Guid FK to hr.employees(Id), raw-SQL at G9-08.
        builder.HasIndex(e => e.ApprovalStatus);
    }
}
