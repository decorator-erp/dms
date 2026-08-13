using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Documents.Infrastructure.Persistence;

/// <summary>Design-time only, so `dotnet ef migrations add` can construct <see cref="DocumentsDbContext"/>
/// without needing the Api host to run it. The connection string here is never used at runtime — the
/// real composition root passes its own via <c>AddDbContext</c>.</summary>
internal sealed class DocumentsDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new DocumentsDbContext(builder.Options);
    }
}
