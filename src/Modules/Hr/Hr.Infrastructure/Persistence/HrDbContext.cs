using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Hr.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hr.Infrastructure.Persistence;

/// <summary>
/// Kept as <c>HrDbContext</c>, not <c>HrModuleDbContext</c> — unlike Identity, there is no
/// <c>Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext&lt;,,&gt;</c> base type to
/// collide with here, so the shorter name reads fine. Deliberate asymmetry with
/// <c>IdentityModuleDbContext</c>, noted at G0-07's review and left as-is.
/// </summary>
internal sealed class HrDbContext(DbContextOptions<HrDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<EmployeeDepartment> EmployeeDepartments => Set<EmployeeDepartment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("hr");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HrDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
