using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>Ported from <c>../backend/Infrastructure/Persistence/Configurations/CostCategoryConfiguration.cs</c>.
/// The only finance table with no money column, and the only one that is global master data — no
/// project, no cross-schema id, nothing to point outward at.</summary>
internal sealed class CostCategoryConfiguration : IEntityTypeConfiguration<CostCategory>
{
    public void Configure(EntityTypeBuilder<CostCategory> builder)
    {
        builder.ToTable("cost_categories");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);

        // Unique globally, matching the backend. Not filtered on IsActive: deactivating a category must
        // NOT free its code for reuse, because budget lines already filed under it keep pointing here
        // and a reused code would silently merge two different cost classifications in every report.
        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("ix_cost_categories_code");
    }
}
