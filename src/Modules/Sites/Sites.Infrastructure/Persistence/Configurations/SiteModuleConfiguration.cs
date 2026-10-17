using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain.Definition;

namespace Sites.Infrastructure.Persistence.Configurations;

/// <summary>
/// Global master data — no query filter, and no project column to scope by. Deactivating a module hides
/// it from the form builder without deleting the records captured under it, which is why reads filter
/// <c>IsActive</c> explicitly rather than relying on a global filter here.
/// </summary>
internal sealed class SiteModuleConfiguration : IEntityTypeConfiguration<SiteModule>
{
    public void Configure(EntityTypeBuilder<SiteModule> builder)
    {
        builder.ToTable("site_modules", t => t.HasCheckConstraint("ck_site_modules_sort", "\"SortOrder\" >= 0"));

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        // Two admins reordering one module's fields in the form builder is exactly the collision this
        // exists for -- and, unlike the backend, the handlers actually translate it (defect B6, S1-06).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DescriptionEn).HasMaxLength(512);
        builder.Property(x => x.DescriptionAr).HasMaxLength(512);

        // ux_site_modules_code (lower("Code")) and ck_site_modules_code_blank are raw SQL in the initial
        // migration -- case-insensitive uniqueness needs a functional index EF cannot express.
    }
}
