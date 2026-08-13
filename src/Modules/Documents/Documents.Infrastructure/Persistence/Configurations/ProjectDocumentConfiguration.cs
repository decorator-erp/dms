using BuildingBlocks;
using Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Documents.Infrastructure.Persistence.Configurations;

internal sealed class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> builder)
    {
        builder.ToTable("project_documents");
        builder.ConfigureEntityConventions();

        builder.Property(d => d.FileName).HasMaxLength(512).IsRequired();
        builder.Property(d => d.StoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(256).IsRequired();
        builder.Property(d => d.FileExtension).HasMaxLength(32);
        builder.Property(d => d.Description);

        // Optimistic concurrency via Postgres' own system column - protects a lost update between two
        // concurrent writers on the same row (e.g. a move racing a lock), ported from the backend.
        // Npgsql's EF Core provider (10.0.3) has no dedicated UseXminAsConcurrencyToken helper - this is
        // the standard manual shadow-property mapping: xmin is DB-generated on every insert/update, read
        // back and compared, never written by EF itself.
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        // ProjectId/UploadedById: bare Guid FKs to projects.projects/hr.employees, raw-SQL constraints
        // in the migration (G7-04) - cross-schema, EF cannot emit them. ProjectStageId/ProjectTaskId:
        // same, but nullable.
        builder.HasIndex(d => d.ProjectId);
        builder.HasIndex(d => d.FolderId);
        builder.HasIndex(d => d.ProjectStageId).HasFilter("\"ProjectStageId\" IS NOT NULL");
        builder.HasIndex(d => d.ProjectTaskId).HasFilter("\"ProjectTaskId\" IS NOT NULL");
        builder.HasIndex(d => d.UploadedById);

        // Lineage: exactly one row per (root, version), and a descending index for "give me the latest
        // version in this lineage" lookups (UploadDocumentRevisionCommand, the review-eligibility check).
        builder.HasIndex(d => new { d.RootDocumentId, d.Version }).IsUnique();
        builder.HasIndex(d => new { d.RootDocumentId, d.Version })
            .HasDatabaseName("ix_project_documents_root_version_desc")
            .IsDescending(false, true);

        // FolderId (same schema, a real EF-managed FK) is configured from DocumentFolder's own side
        // (HasMany(f => f.Documents).WithOne()...) - see DocumentFolderConfiguration, and
        // StageTeamConfiguration/StageTeamMemberConfiguration for the identical parent-configures-the-
        // relationship split already established in Projects.

        // RootDocumentId: same-schema self-reference (this table to itself), a real EF-managed FK - the
        // lineage pointer, not a cross-schema concern. A v1 document's RootDocumentId equals its own Id
        // (ProjectDocument.Create), so this FK legitimately points a row at itself; Postgres checks FK
        // constraints at statement end, not per-row, so a single INSERT setting both columns to the same
        // value satisfies it. Cascade, matching the backend: hard-deleting the root document (this port
        // never does - deletion is always the soft Deactivate() flip, G7-02's correction 4) takes its
        // whole revision chain with it rather than leaving orphaned FKs, should a future hard-delete path
        // ever exist. No inverse navigation - nothing in this module needs "all revisions of this root"
        // as a loaded collection; IDocumentQueries/the lineage queries go through the DbContext directly.
        builder.HasOne<ProjectDocument>()
            .WithMany()
            .HasForeignKey(d => d.RootDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
