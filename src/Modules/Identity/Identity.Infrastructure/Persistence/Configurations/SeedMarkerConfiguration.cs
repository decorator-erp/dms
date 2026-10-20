using Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class SeedMarkerConfiguration : IEntityTypeConfiguration<SeedMarker>
{
    /// <summary>The key's primary key, named so a concurrent seed's 23505 can be told apart.</summary>
    public const string PrimaryKey = "PK_seed_markers";

    public void Configure(EntityTypeBuilder<SeedMarker> builder)
    {
        builder.ToTable("seed_markers");
        builder.HasKey(m => m.Key).HasName(PrimaryKey);
        builder.Property(m => m.Key).HasMaxLength(100);
    }
}
