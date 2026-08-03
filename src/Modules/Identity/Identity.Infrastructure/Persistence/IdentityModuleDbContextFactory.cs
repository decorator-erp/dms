using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Identity.Infrastructure.Persistence;

/// <summary>Design-time only, so `dotnet ef migrations add` can construct
/// <see cref="IdentityModuleDbContext"/> without needing the Api host to run it. The connection string
/// here is never used at runtime — the real composition root passes its own via
/// <c>AddDbContext</c>.</summary>
internal sealed class IdentityModuleDbContextFactory : IDesignTimeDbContextFactory<IdentityModuleDbContext>
{
    public IdentityModuleDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IdentityModuleDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new IdentityModuleDbContext(builder.Options);
    }
}
