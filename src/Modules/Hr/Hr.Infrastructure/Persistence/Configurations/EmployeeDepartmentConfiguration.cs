using Hr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hr.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeDepartmentConfiguration : IEntityTypeConfiguration<EmployeeDepartment>
{
    public void Configure(EntityTypeBuilder<EmployeeDepartment> builder)
    {
        builder.ToTable("employee_departments");

        // Composite key on the pair itself - EmployeeDepartment isn't a SharedKernel.Entity (no
        // surrogate Guid Id), so ConfigureEntityConventions<T>() doesn't apply here.
        builder.HasKey(ed => new { ed.EmployeeId, ed.DepartmentId });

        builder.Property(ed => ed.CreatedAtUtc).IsRequired();

        // Both Employee and Department are in THIS module's own model - unlike Employee.AppUserId,
        // these two ARE ordinary same-model foreign keys, and EF emits them normally.
        builder.HasOne<Employee>().WithMany().HasForeignKey(ed => ed.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Department>().WithMany().HasForeignKey(ed => ed.DepartmentId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ed => ed.DepartmentId);
    }
}
