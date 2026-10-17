using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Approvals.Infrastructure.Persistence;

/// <summary>Design-time only, so `dotnet ef migrations add` can construct <see cref="ApprovalsDbContext"/>
/// without needing the Api host to run it. The connection string here is never used at runtime — the
/// real composition root passes its own via <c>AddDbContext</c>.</summary>
internal sealed class ApprovalsDbContextFactory : IDesignTimeDbContextFactory<ApprovalsDbContext>
{
    public ApprovalsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ApprovalsDbContext> builder = new DbContextOptionsBuilder<ApprovalsDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new ApprovalsDbContext(builder.Options);
    }
}
