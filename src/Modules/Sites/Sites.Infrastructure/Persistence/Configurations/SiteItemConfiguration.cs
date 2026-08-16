using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain.Definition;

namespace Sites.Infrastructure.Persistence.Configurations;

internal sealed class SiteItemConfiguration : IEntityTypeConfiguration<SiteItem>
{
    public void Configure(EntityTypeBuilder<SiteItem> builder)
    {
        builder.ToTable("site_items", t =>
        {
            t.HasCheckConstraint("ck_site_items_type_range", "\"DataType\" BETWEEN 1 AND 5");
        });

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DataType).HasConversion<int>();
        builder.Property(x => x.Unit).HasMaxLength(32);
        builder.Property(x => x.MinValue).HasColumnType("numeric(18,4)");
        builder.Property(x => x.MaxValue).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Tolerance).HasColumnType("numeric(18,3)");
        builder.Property(x => x.RegexPattern).HasMaxLength(256);

        // MaxLength's own ceiling is 1024 because that is site_record_values."ValueText"'s width -- a
        // larger promise would be broken by the column as a 500 rather than by SiteItem.Create as a 400.
        // Enforced in ck_site_items_range (raw SQL), alongside ux_site_items_code (lower("Code")) and
        // ck_site_items_code_blank.
    }
}
