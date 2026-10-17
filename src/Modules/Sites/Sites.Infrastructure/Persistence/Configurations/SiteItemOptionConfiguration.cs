using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain.Definition;

namespace Sites.Infrastructure.Persistence.Configurations;

internal sealed class SiteItemOptionConfiguration : IEntityTypeConfiguration<SiteItemOption>
{
    public void Configure(EntityTypeBuilder<SiteItemOption> builder)
    {
        builder.ToTable("site_item_options", t => t.HasCheckConstraint("ck_site_item_options_sort", "\"SortOrder\" >= 0"));

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(128).IsRequired();

        builder.HasOne<SiteItem>()
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Raw SQL in the initial migration: ux_site_item_options_item_code (ItemId, lower("Code")),
        // ck_site_item_options_code_blank, and uq_site_item_options_item_id UNIQUE ("ItemId", "Id") --
        // the last exists only so site_record_values' composite (ItemId, OptionId) FK has a unique key
        // to point at. Without it Postgres rejects the FK outright.
    }
}
