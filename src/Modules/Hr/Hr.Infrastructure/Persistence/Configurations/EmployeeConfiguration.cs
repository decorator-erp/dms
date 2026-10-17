using BuildingBlocks;
using Hr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hr.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.ConfigureEntityConventions();

        builder.Property(e => e.JobTitle).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.ContractType).HasConversion<int>();
        builder.Property(e => e.PhoneNumber).HasMaxLength(50);
        builder.Property(e => e.EmergencyContactPhone).HasMaxLength(50);

        // AppUserId is a bare string FK to identity.users(id) - no navigation, no HasOne (the two
        // entities are in different EF models). EF emits the unique index (an ordinary same-schema
        // index, nothing cross-schema about it); the actual FK CONSTRAINT is added via raw SQL in the
        // migration instead, since EF cannot emit a constraint across models it doesn't know are related
        // (see docs/cross-schema-foreign-keys.md). ResolveEmployeeIdAsync (G2-06) filters on this column
        // constantly - the index is what makes that the hot path it's designed to be.
        builder.Property(e => e.AppUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(e => e.AppUserId).IsUnique();
    }
}
