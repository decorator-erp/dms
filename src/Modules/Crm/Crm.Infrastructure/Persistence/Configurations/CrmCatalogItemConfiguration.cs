using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmCatalogItemConfiguration : IEntityTypeConfiguration<CrmCatalogItem>
{
    public void Configure(EntityTypeBuilder<CrmCatalogItem> builder)
    {
        builder.ToTable("crm_catalog_items");
        builder.ConfigureEntityConventions();

        builder.Property(i => i.Code).HasMaxLength(64).IsRequired();
        builder.Property(i => i.NameEn).HasMaxLength(256).IsRequired();
        builder.Property(i => i.NameAr).HasMaxLength(256).IsRequired();
        builder.Property(i => i.DescriptionEn).HasMaxLength(1000);
        builder.Property(i => i.DescriptionAr).HasMaxLength(1000);
        builder.Property(i => i.ImageUri).HasMaxLength(1024);

        builder.HasOne<CrmCatalogCategory>().WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.CategoryId);

        builder.HasIndex(i => i.Code).IsUnique();
    }
}
