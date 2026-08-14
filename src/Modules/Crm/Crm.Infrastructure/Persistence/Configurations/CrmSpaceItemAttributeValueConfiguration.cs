using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmSpaceItemAttributeValueConfiguration : IEntityTypeConfiguration<CrmSpaceItemAttributeValue>
{
    public void Configure(EntityTypeBuilder<CrmSpaceItemAttributeValue> builder)
    {
        builder.ToTable("crm_space_item_attribute_values");
        builder.ConfigureEntityConventions();

        builder.Property(v => v.ValueText).HasMaxLength(1000);
        builder.Property(v => v.ValueNumber).HasColumnType("numeric(18,4)");

        builder.HasOne<CrmSpaceItem>().WithMany().HasForeignKey(v => v.SpaceItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CrmCatalogAttribute>().WithMany().HasForeignKey(v => v.AttributeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrmCatalogAttributeOption>().WithMany().HasForeignKey(v => v.OptionId).OnDelete(DeleteBehavior.Restrict);

        // One override row per (item, attribute) - corrections are replace-all.
        builder.HasIndex(v => new { v.SpaceItemId, v.AttributeId }).IsUnique();
    }
}
