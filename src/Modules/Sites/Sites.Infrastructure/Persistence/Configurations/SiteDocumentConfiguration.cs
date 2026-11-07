using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain;

namespace Sites.Infrastructure.Persistence.Configurations;

internal sealed class SiteDocumentConfiguration : IEntityTypeConfiguration<SiteDocument>
{
    public void Configure(EntityTypeBuilder<SiteDocument> builder)
    {
        // S1: unlink is a soft delete (SiteDocument.Deactivate), so every read -- the gallery, the
        // unlink lookup, the duplicate pre-check -- must see active links only. Without this filter an
        // unlinked attachment kept listing and a second unlink "succeeded".
        builder.HasQueryFilter(x => x.IsActive);

        builder.ToTable("site_documents", t =>
        {
            // The database twin of SiteDocument.Create's XOR.
            t.HasCheckConstraint(
                "ck_site_documents_one_subject", "num_nonnulls(\"SiteId\", \"RecordId\") = 1");

            t.HasCheckConstraint("ck_site_documents_kind_range", "\"Kind\" BETWEEN 1 AND 3");
        });

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.Kind).HasConversion<int>();

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SiteRecord>()
            .WithMany()
            .HasForeignKey(x => x.RecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DocumentId).HasDatabaseName("ix_site_documents_document");

        builder.HasIndex(x => x.SiteId)
            .HasDatabaseName("ix_site_documents_site")
            .HasFilter("\"SiteId\" IS NOT NULL");

        builder.HasIndex(x => x.RecordId)
            .HasDatabaseName("ix_site_documents_record")
            .HasFilter("\"RecordId\" IS NOT NULL");

        builder.HasIndex(x => x.AddedById).HasDatabaseName("ix_site_documents_added_by");

        // DocumentId -> documents.project_documents and AddedById -> hr.employees are cross-schema FKs
        // added by raw SQL at S1-05, both ON DELETE RESTRICT. The backend makes DocumentId CASCADE;
        // arch rule 9 forbids CASCADE across a schema boundary, so this port diverges deliberately.
    }
}
