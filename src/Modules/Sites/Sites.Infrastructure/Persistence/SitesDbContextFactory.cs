using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sites.Infrastructure.Persistence;

/// <summary>Design-time only, so <c>dotnet ef migrations add</c> can construct
/// <see cref="SitesDbContext"/> without needing the Api host to run it. The connection string here is
/// never used at runtime — the real composition root passes its own via <c>AddDbContext</c>.</summary>
internal sealed class SitesDbContextFactory : IDesignTimeDbContextFactory<SitesDbContext>
{
    public SitesDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SitesDbContext> builder = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new SitesDbContext(builder.Options);
    }
}
