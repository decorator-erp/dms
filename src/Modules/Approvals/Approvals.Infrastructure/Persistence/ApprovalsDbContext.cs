using Approvals.Domain;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Approvals.Infrastructure.Persistence;

internal sealed class ApprovalsDbContext(DbContextOptions<ApprovalsDbContext> options) : DbContext(options)
{
    public DbSet<Approval> Approvals => Set<Approval>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("approvals");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApprovalsDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
    }
}
