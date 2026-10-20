using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Hr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

    // A11. Employee and Department carry an IsActive query filter (K3) and are the required principals of
    // EmployeeDepartment, which has none, so EF warns that a membership could load with its principal
    // filtered out. Nothing here navigates from a membership to its employee or department (there are no
    // navigations at all); memberships are always read by key and joined explicitly, filters included or
    // ignored at the call site. Turned off for this one event on this one context, not globally.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("hr");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HrDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
