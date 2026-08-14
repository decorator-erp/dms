using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmArtifactDocumentConfiguration : IEntityTypeConfiguration<CrmArtifactDocument>
{
    public void Configure(EntityTypeBuilder<CrmArtifactDocument> builder)
    {
        builder.ToTable("crm_artifact_documents");
        builder.ConfigureEntityConventions();

        // DocumentId: bare Guid, deliberately NO index-backed FK - structural item 1, the
        // migration-order cycle (crm -> documents would require documents before crm, but
        // projects -> crm requires crm before projects, and documents already requires projects
        // before it). Validated in Crm.Application against Documents.Contracts.IDocumentQueries at
        // artifact-creation time (G9-12) - see docs/cross-schema-foreign-keys.md's CRM entry (G9-08)
        // for the full reasoning. Still indexed for lookup, just not constrained.
        builder.HasIndex(a => a.DocumentId);

        builder.HasOne<CrmDesignBrief>().WithMany().HasForeignKey(a => a.BriefId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CrmSpace>().WithMany().HasForeignKey(a => a.SpaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CrmRequirementItem>().WithMany().HasForeignKey(a => a.RequirementItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CrmMeeting>().WithMany().HasForeignKey(a => a.MeetingId).OnDelete(DeleteBehavior.Cascade);

        // AddedByContactId: same-schema, a real EF-managed FK. AddedByEmployeeId: bare Guid FK to
        // hr.employees(Id) - raw-SQL constraint at G9-08.
        builder.HasOne<CrmContact>().WithMany().HasForeignKey(a => a.AddedByContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.AddedByEmployeeId).HasFilter("\"AddedByEmployeeId\" IS NOT NULL");
    }
}
