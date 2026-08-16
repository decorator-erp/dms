using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain.Definition;

namespace Sites.Infrastructure.Persistence.Configurations;

internal sealed class SiteModuleItemConfiguration : IEntityTypeConfiguration<SiteModuleItem>
{
    public void Configure(EntityTypeBuilder<SiteModuleItem> builder)
    {
        builder.ToTable("site_module_items", t =>
        {
            t.HasCheckConstraint("ck_site_module_items_sort", "\"SortOrder\" >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<SiteModule>()
            .WithMany()
            .HasForeignKey(x => x.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // RESTRICT, unlike the module side: an item is shared across modules, so deleting one out from
        // under a link would silently empty another module's form. ItemInUse is the friendly refusal.
        builder.HasOne<SiteItem>()
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique across ACTIVE and inactive links alike -- deliberately not partial. Re-attaching a
        // detached item must reuse its existing row (reactivating it, preserving the captured values it
        // still governs) rather than insert a second link for the same pair.
        builder.HasIndex(x => new { x.ModuleId, x.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_site_module_items_module_item");

        builder.HasIndex(x => new { x.ModuleId, x.SortOrder })
            .HasDatabaseName("ix_site_module_items_module_sort");

        builder.HasIndex(x => x.ItemId).HasDatabaseName("ix_site_module_items_item");
    }
}
