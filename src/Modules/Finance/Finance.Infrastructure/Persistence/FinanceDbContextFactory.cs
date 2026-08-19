using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.Infrastructure.Persistence;

/// <summary>Design-time only, so <c>dotnet ef migrations add</c> can construct
/// <see cref="FinanceDbContext"/> without needing the Api host to run it. The connection string here is
/// never used at runtime — the real composition root passes its own via <c>AddDbContext</c>.</summary>
internal sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new FinanceDbContext(builder.Options);
    }
}
