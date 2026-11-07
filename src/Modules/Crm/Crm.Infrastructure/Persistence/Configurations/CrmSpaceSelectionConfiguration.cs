using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmSpaceSelectionConfiguration : IEntityTypeConfiguration<CrmSpaceSelection>
{
    public void Configure(EntityTypeBuilder<CrmSpaceSelection> builder)
    {
        builder.ToTable("crm_space_selections");
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Quantity).HasPrecision(18, 3);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(s => new { s.SpaceId, s.SortOrder });
        builder.HasIndex(s => s.CatalogItemId);
        builder.HasOne<CrmSpace>().WithMany().HasForeignKey(s => s.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrmCatalogItem>().WithMany().HasForeignKey(s => s.CatalogItemId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Options).WithOne().HasForeignKey(o => o.SelectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CrmSpaceSelectionOptionConfiguration : IEntityTypeConfiguration<CrmSpaceSelectionOption>
{
    public void Configure(EntityTypeBuilder<CrmSpaceSelectionOption> builder)
    {
        builder.ToTable("crm_space_selection_options");
        builder.ConfigureEntityConventions();

        builder.HasIndex(o => new { o.SelectionId, o.AttributeId }).IsUnique();
        builder.HasIndex(o => o.OptionId);
        builder.HasOne<CrmCatalogAttribute>().WithMany().HasForeignKey(o => o.AttributeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CrmCatalogAttributeOption>().WithMany().HasForeignKey(o => o.OptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
