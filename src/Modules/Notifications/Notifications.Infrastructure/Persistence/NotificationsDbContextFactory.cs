using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Notifications.Infrastructure.Persistence;

/// <summary>Design-time only, so <c>dotnet ef migrations add</c> can generate the <c>notify</c> schema's
/// migrations without booting the host.</summary>
internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=postgres");

        return new NotificationsDbContext(builder.Options);
    }
}
