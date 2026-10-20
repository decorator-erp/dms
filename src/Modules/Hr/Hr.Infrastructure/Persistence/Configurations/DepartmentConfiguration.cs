using BuildingBlocks;
using Hr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hr.Infrastructure.Persistence.Configurations;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    /// <summary>Named so a race on it can be mapped (Aug 3.5). The name EF already gave it.</summary>
    public const string UniqueActiveNameIndex = "IX_departments_Name";

    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.ConfigureEntityConventions();

        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2000);
        // N8: a name is unique among ACTIVE departments only, so a deleted department's name can be
        // reused. A plain unique index counted deleted rows forever.
        builder.HasIndex(d => d.Name).IsUnique().HasFilter("\"IsActive\"").HasDatabaseName(UniqueActiveNameIndex);

        // K3: a deleted (deactivated) department is invisible by default; see EmployeeConfiguration.
        builder.HasQueryFilter(d => d.IsActive);

        // DepartmentHeadId references Employee, another aggregate IN THIS SAME MODULE. Still no
        // navigation (Department doesn't own the employee's lifecycle), but since Aug 2.7 a real foreign
        // key: RESTRICT, so a head can never point at a row that doesn't exist. Same schema, so EF emits it;
        // only fk_employees_app_user, which crosses into identity, has to be written by hand.
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.DepartmentHeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.DepartmentHeadId);
    }
}
