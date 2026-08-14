using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmCatalogCategoryConfiguration : IEntityTypeConfiguration<CrmCatalogCategory>
{
    public void Configure(EntityTypeBuilder<CrmCatalogCategory> builder)
    {
        builder.ToTable("crm_catalog_categories");
        builder.ConfigureEntityConventions();

        builder.Property(c => c.Code).HasMaxLength(64).IsRequired();
        builder.Property(c => c.NameEn).HasMaxLength(128).IsRequired();
        builder.Property(c => c.NameAr).HasMaxLength(128).IsRequired();
        builder.Property(c => c.DescriptionEn).HasMaxLength(1000);
        builder.Property(c => c.DescriptionAr).HasMaxLength(1000);
        builder.Property(c => c.ImageUri).HasMaxLength(1024);
        builder.Property(c => c.SelectionMode).HasConversion<int>();

        // varchar(1024): 27 levels x 37 chars/level (36-char guid + '/'), matching CrmCatalogCategory.MaxDepth.
        builder.Property(c => c.Path).HasMaxLength(1024).IsRequired();

        builder.HasOne<CrmCatalogCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.ParentId).HasFilter("\"ParentId\" IS NOT NULL");

        builder.HasIndex(c => c.Code).IsUnique();

        // Materialized-path descendant lookups (LIKE path || '%') want an index on Path itself.
        builder.HasIndex(c => c.Path);
    }
}
