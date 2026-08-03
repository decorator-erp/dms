using BuildingBlocks;
using Hr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hr.Infrastructure.Persistence.Configurations;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.ConfigureEntityConventions();

        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2000);
        builder.HasIndex(d => d.Name).IsUnique();

        // DepartmentHeadId references Employee, another aggregate IN THIS SAME MODULE - a plain
        // indexed column, not a HasOne/navigation, since Department doesn't own the employee's
        // lifecycle and shouldn't be able to cascade into it.
        builder.HasIndex(d => d.DepartmentHeadId);
    }
}
