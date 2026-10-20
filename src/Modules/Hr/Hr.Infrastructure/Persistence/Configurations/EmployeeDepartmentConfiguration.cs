using Hr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hr.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeDepartmentConfiguration : IEntityTypeConfiguration<EmployeeDepartment>
{
    public const string ActiveMembershipIndex = "IX_employee_departments_EmployeeId";

    public void Configure(EntityTypeBuilder<EmployeeDepartment> builder)
    {
        builder.ToTable("employee_departments");

        // Composite key on the pair itself - EmployeeDepartment isn't a SharedKernel.Entity (no
        // surrogate Guid Id), so ConfigureEntityConventions<T>() doesn't apply here.
        builder.HasKey(ed => new { ed.EmployeeId, ed.DepartmentId });

        builder.Property(ed => ed.CreatedAtUtc).IsRequired();
        builder.Property(ed => ed.StartedAtUtc).IsRequired();

        // Both Employee and Department are in THIS module's own model - unlike Employee.AppUserId,
        // these two ARE ordinary same-model foreign keys, and EF emits them normally. RESTRICT since Aug
        // 2.7: nothing hard-deletes employees or departments (both are soft-deleted), and if something
        // ever tried, silently deleting the membership history with them would be the wrong answer.
        builder.HasOne<Employee>().WithMany().HasForeignKey(ed => ed.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(ed => ed.DepartmentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ed => ed.DepartmentId);

        // K1: at most one ACTIVE membership per employee. DepartmentMembershipService checks first and
        // maps a lost race on this index (23505, by name) to employees.AlreadyInAnotherDepartment.
        builder.HasIndex(ed => ed.EmployeeId).IsUnique().HasFilter("\"IsActive\"")
            .HasDatabaseName(ActiveMembershipIndex);
    }
}
