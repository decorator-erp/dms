using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Projects.Infrastructure.Persistence;

/// <summary>Design-time only, so `dotnet ef migrations add` can construct <see cref="ProjectsDbContext"/>
/// without needing the Api host to run it. The connection string here is never used at runtime — the
/// real composition root passes its own via <c>AddDbContext</c>.</summary>
internal sealed class ProjectsDbContextFactory : IDesignTimeDbContextFactory<ProjectsDbContext>
{
    public ProjectsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ProjectsDbContext> builder = new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new ProjectsDbContext(builder.Options);
    }
}
