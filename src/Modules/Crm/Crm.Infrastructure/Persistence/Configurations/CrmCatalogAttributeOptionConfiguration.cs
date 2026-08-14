using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmCatalogAttributeOptionConfiguration : IEntityTypeConfiguration<CrmCatalogAttributeOption>
{
    public void Configure(EntityTypeBuilder<CrmCatalogAttributeOption> builder)
    {
        builder.ToTable("crm_catalog_attribute_options");
        builder.ConfigureEntityConventions();

        builder.Property(o => o.Code).HasMaxLength(64).IsRequired();
        builder.Property(o => o.NameEn).HasMaxLength(128).IsRequired();
        builder.Property(o => o.NameAr).HasMaxLength(128).IsRequired();
        builder.Property(o => o.ImageUri).HasMaxLength(1024);

        builder.HasOne<CrmCatalogAttribute>().WithMany().HasForeignKey(o => o.AttributeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => o.AttributeId);

        builder.HasIndex(o => new { o.AttributeId, o.Code }).IsUnique();
    }
}
