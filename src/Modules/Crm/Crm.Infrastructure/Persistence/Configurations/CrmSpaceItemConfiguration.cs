using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmSpaceItemConfiguration : IEntityTypeConfiguration<CrmSpaceItem>
{
    public void Configure(EntityTypeBuilder<CrmSpaceItem> builder)
    {
        builder.ToTable("crm_space_items");
        builder.ConfigureEntityConventions();

        builder.Property(i => i.CustomName).HasMaxLength(256);
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.Note).HasMaxLength(1000);

        builder.HasOne<CrmSpace>().WithMany().HasForeignKey(i => i.SpaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => i.SpaceId);

        builder.HasOne<CrmCatalogItem>().WithMany().HasForeignKey(i => i.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrmCatalogCategory>().WithMany().HasForeignKey(i => i.CustomCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrmBriefSpace>().WithMany().HasForeignKey(i => i.SourceBriefSpaceId).OnDelete(DeleteBehavior.SetNull);

        // A catalog item is recorded once per space (ux_crm_space_items_space_item, active rows only) -
        // partial unique index so a soft-removed row frees the slot for a re-add.
        builder.HasIndex(i => new { i.SpaceId, i.CatalogItemId })
            .HasDatabaseName("ux_crm_space_items_space_item")
            .IsUnique()
            .HasFilter("\"CatalogItemId\" IS NOT NULL AND \"IsActive\" = true");
    }
}
