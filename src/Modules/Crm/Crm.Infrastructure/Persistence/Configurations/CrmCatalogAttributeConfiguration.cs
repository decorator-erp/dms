using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmCatalogAttributeConfiguration : IEntityTypeConfiguration<CrmCatalogAttribute>
{
    public void Configure(EntityTypeBuilder<CrmCatalogAttribute> builder)
    {
        builder.ToTable("crm_catalog_attributes");
        builder.ConfigureEntityConventions();

        builder.Property(a => a.Code).HasMaxLength(64).IsRequired();
        builder.Property(a => a.NameEn).HasMaxLength(128).IsRequired();
        builder.Property(a => a.NameAr).HasMaxLength(128).IsRequired();
        builder.Property(a => a.DataType).HasConversion<int>();
        builder.Property(a => a.Unit).HasMaxLength(32);

        builder.HasOne<CrmCatalogCategory>().WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.CategoryId);

        builder.HasIndex(a => new { a.CategoryId, a.Code }).IsUnique();
    }
}
