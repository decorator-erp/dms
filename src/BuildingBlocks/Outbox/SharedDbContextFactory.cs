using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Design-time only, so `dotnet ef migrations add` can construct <see cref="SharedDbContext"/> without
/// needing the Api host to exist yet. The connection string here is never used at runtime — the real
/// composition root passes its own via <c>AddDbContext</c>.
/// </summary>
internal sealed class SharedDbContextFactory : IDesignTimeDbContextFactory<SharedDbContext>
{
    public SharedDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SharedDbContext> builder = new DbContextOptionsBuilder<SharedDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new SharedDbContext(builder.Options);
    }
}
