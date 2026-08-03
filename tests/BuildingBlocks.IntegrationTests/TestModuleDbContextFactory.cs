using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BuildingBlocks.IntegrationTests;

/// <summary>Design-time only, so `dotnet ef migrations add` can generate the "side_effects" table's
/// migration without a real host project.</summary>
internal sealed class TestModuleDbContextFactory : IDesignTimeDbContextFactory<TestModuleDbContext>
{
    public TestModuleDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<TestModuleDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new TestModuleDbContext(builder.Options);
    }
}
