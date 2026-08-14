using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmCatalogCategorySpaceTypeConfiguration : IEntityTypeConfiguration<CrmCatalogCategorySpaceType>
{
    public void Configure(EntityTypeBuilder<CrmCatalogCategorySpaceType> builder)
    {
        builder.ToTable("crm_catalog_category_space_types");
        builder.ConfigureEntityConventions();

        builder.HasOne<CrmCatalogCategory>().WithMany().HasForeignKey(m => m.CategoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CrmSpaceType>().WithMany().HasForeignKey(m => m.SpaceTypeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.CategoryId, m.SpaceTypeId }).IsUnique();
    }
}
