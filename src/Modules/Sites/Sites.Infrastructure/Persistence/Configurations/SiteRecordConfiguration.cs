using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain;
using Sites.Domain.Definition;

namespace Sites.Infrastructure.Persistence.Configurations;

internal sealed class SiteRecordConfiguration : IEntityTypeConfiguration<SiteRecord>
{
    public void Configure(EntityTypeBuilder<SiteRecord> builder)
    {
        builder.HasQueryFilter(x => x.IsActive);

        builder.ToTable("site_records", t =>
        {
            t.HasCheckConstraint("ck_site_records_sort", "\"SortOrder\" >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Note).HasColumnType("text");

        // Intra-schema FKs, declared without navigation properties (the entities carry bare Guids).
        // ModuleId is RESTRICT, not CASCADE: deleting a module out from under captured records is the
        // thing ModuleInUse exists to refuse, and the constraint is the backstop for that refusal.
        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SiteModule>()
            .WithMany()
            .HasForeignKey(x => x.ModuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SiteId, x.ModuleId, x.SortOrder })
            .HasDatabaseName("ix_site_records_site_module");

        builder.HasIndex(x => x.ModuleId).HasDatabaseName("ix_site_records_module");

        // SpaceId and CatalogItemId both point into the crm schema and both carry real FKs at S1-05 --
        // crm precedes sites in SchemaOrder, so unlike CRM's own artifact link there is no ordering
        // cycle to work around. Filtered indexes: most records pin neither.
        builder.HasIndex(x => x.SpaceId)
            .HasDatabaseName("ix_site_records_space")
            .HasFilter("\"SpaceId\" IS NOT NULL");

        builder.HasIndex(x => x.CatalogItemId)
            .HasDatabaseName("ix_site_records_catalog_item")
            .HasFilter("\"CatalogItemId\" IS NOT NULL");

        // ux_site_records_label -- unique on (SiteId, ModuleId, lower("Label")) WHERE "IsActive" -- is
        // raw SQL in the initial migration. Functional and partial, so EF cannot express it, and the
        // partiality is load-bearing: soft-deleting a record must free its label for reuse.
    }
}
