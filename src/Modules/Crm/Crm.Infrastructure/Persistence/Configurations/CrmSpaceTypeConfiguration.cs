using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmSpaceTypeConfiguration : IEntityTypeConfiguration<CrmSpaceType>
{
    public void Configure(EntityTypeBuilder<CrmSpaceType> builder)
    {
        builder.ToTable("crm_space_types");
        builder.ConfigureEntityConventions();

        builder.Property(t => t.Code).HasMaxLength(64).IsRequired();
        builder.Property(t => t.NameEn).HasMaxLength(128).IsRequired();
        builder.Property(t => t.NameAr).HasMaxLength(128).IsRequired();

        builder.HasIndex(t => t.Code).IsUnique();
    }
}
