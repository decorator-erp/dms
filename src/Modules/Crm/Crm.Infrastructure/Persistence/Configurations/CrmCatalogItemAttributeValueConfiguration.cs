using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmCatalogItemAttributeValueConfiguration : IEntityTypeConfiguration<CrmCatalogItemAttributeValue>
{
    public void Configure(EntityTypeBuilder<CrmCatalogItemAttributeValue> builder)
    {
        builder.ToTable("crm_catalog_item_attribute_values");
        builder.ConfigureEntityConventions();

        builder.Property(v => v.ValueText).HasMaxLength(1000);
        builder.Property(v => v.ValueNumber).HasColumnType("numeric(18,4)");

        builder.HasOne<CrmCatalogItem>().WithMany().HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CrmCatalogAttribute>().WithMany().HasForeignKey(v => v.AttributeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrmCatalogAttributeOption>().WithMany().HasForeignKey(v => v.OptionId).OnDelete(DeleteBehavior.Restrict);

        // One row per (item, attribute) - corrections are replace-all.
        builder.HasIndex(v => new { v.ItemId, v.AttributeId }).IsUnique();
    }
}
