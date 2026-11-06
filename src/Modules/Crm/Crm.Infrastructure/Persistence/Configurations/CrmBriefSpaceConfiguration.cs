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

        // Same xmin concurrency token as CrmDesignBriefConfiguration.
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        builder.Property(e => e.Requirements).HasMaxLength(2000);
        builder.Property(e => e.Selections).HasColumnType("jsonb");
        builder.Property(e => e.ApprovalStatus).HasConversion<int>();

        builder.HasOne<CrmDesignBrief>().WithMany().HasForeignKey(e => e.BriefId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.BriefId);

        builder.HasOne<CrmSpace>().WithMany().HasForeignKey(e => e.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.SpaceId).HasFilter("\"SpaceId\" IS NOT NULL");

        // Self-reference: the ledger's supersession chain.
        builder.HasOne<CrmBriefSpace>().WithMany().HasForeignKey(e => e.SupersedesSpaceId).OnDelete(DeleteBehavior.Restrict);

        // One supersession per entry: the ledger is a chain, never a fork. Partial, so the many
        // non-correction entries (NULL) don't collide. SupersedeCrmBriefSpaceCommandHandler maps its
        // violation to crm.briefs.AlreadySuperseded.
        builder.HasIndex(e => e.SupersedesSpaceId).IsUnique().HasFilter("\"SupersedesSpaceId\" IS NOT NULL");

        // MeetingId: same-schema, unvalidated (informational source reference, same reasoning as
        // CrmDesignBrief.LastMeetingId). ProjectTaskId: bare Guid, deliberately NO FK - see
        // CrmMeetingConfiguration's note (crm precedes projects in SchemaOrder; crm -> projects is not
        // in rule 9's allow-list). CreatedById: bare Guid FK to hr.employees(Id) - raw-SQL constraint
        // added at G9-08.
        builder.HasIndex(e => e.ApprovalStatus);
        builder.HasIndex(e => e.CreatedById);
    }
}
