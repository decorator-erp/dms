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

        // L-4: a document is linked to a subject once. The subject is four nullable columns (exactly one
        // set, enforced by CrmArtifactDocument.Create), so one partial unique index per column; the same
        // document may still evidence several subjects. A violation is 409 crm.artifacts.AlreadyLinked
        // (ArtifactWrites). The migration refuses to build them over existing duplicates.
        builder.HasIndex(a => new { a.DocumentId, a.BriefId }).IsUnique()
            .HasFilter("\"BriefId\" IS NOT NULL").HasDatabaseName(ArtifactWrites.BriefIndex);
        builder.HasIndex(a => new { a.DocumentId, a.SpaceId }).IsUnique()
            .HasFilter("\"SpaceId\" IS NOT NULL").HasDatabaseName(ArtifactWrites.SpaceIndex);
        builder.HasIndex(a => new { a.DocumentId, a.RequirementItemId }).IsUnique()
            .HasFilter("\"RequirementItemId\" IS NOT NULL").HasDatabaseName(ArtifactWrites.RequirementItemIndex);
        builder.HasIndex(a => new { a.DocumentId, a.MeetingId }).IsUnique()
            .HasFilter("\"MeetingId\" IS NOT NULL").HasDatabaseName(ArtifactWrites.MeetingIndex);

        // AddedByContactId: same-schema, a real EF-managed FK. AddedByEmployeeId: bare Guid FK to
        // hr.employees(Id) - raw-SQL constraint at G9-08.
        builder.HasOne<CrmContact>().WithMany().HasForeignKey(a => a.AddedByContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.AddedByEmployeeId).HasFilter("\"AddedByEmployeeId\" IS NOT NULL");
    }
}
