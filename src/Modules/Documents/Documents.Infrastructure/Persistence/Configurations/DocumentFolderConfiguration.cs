using BuildingBlocks;
using Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Documents.Infrastructure.Persistence.Configurations;

internal sealed class DocumentFolderConfiguration : IEntityTypeConfiguration<DocumentFolder>
{
    public void Configure(EntityTypeBuilder<DocumentFolder> builder)
    {
        builder.ToTable("document_folders");
        builder.ConfigureEntityConventions();

        builder.Property(f => f.Name).HasMaxLength(256).IsRequired();

        // ProjectId: bare Guid FK to projects.projects, raw-SQL constraint in the migration (G7-04).
        builder.HasIndex(f => f.ProjectId);
        builder.HasIndex(f => f.ProjectStageId).HasFilter("\"ProjectStageId\" IS NOT NULL");
        builder.HasIndex(f => f.ProjectTaskId).HasFilter("\"ProjectTaskId\" IS NOT NULL");

        // No two siblings under the same parent (including "no parent", i.e. root-level) share a name.
        builder.HasIndex(f => new { f.ProjectId, f.ParentFolderId, f.Name }).IsUnique();

        // At most one default folder per stage, and per task - the DB backstop behind
        // DocumentFolder.Create's own defaultAlreadyExistsForScope check (G7-02). Partial: only rows
        // that are both a default AND scoped to that particular column participate.
        builder.HasIndex(f => f.ProjectTaskId)
            .HasDatabaseName("ux_document_folders_default_per_task")
            .IsUnique()
            .HasFilter("\"ProjectTaskId\" IS NOT NULL AND \"IsDefault\" = true");
        builder.HasIndex(f => f.ProjectStageId)
            .HasDatabaseName("ux_document_folders_default_per_stage")
            .IsUnique()
            .HasFilter("\"ProjectStageId\" IS NOT NULL AND \"IsDefault\" = true");

        // SubFolders: same-schema self-reference, a real EF-managed FK. Restrict, not Cascade - deleting
        // a folder with subfolders must fail loudly (or be handled explicitly by the caller), never
        // silently cascade-delete a subtree.
        builder.HasMany(f => f.SubFolders)
            .WithOne()
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Documents: same-schema, a real EF-managed FK. SetNull - a document survives its folder's
        // deletion (matches the backend).
        builder.HasMany(f => f.Documents)
            .WithOne()
            .HasForeignKey(d => d.FolderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
