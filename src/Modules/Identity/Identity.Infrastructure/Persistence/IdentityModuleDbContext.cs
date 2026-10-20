using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Named <c>IdentityModuleDbContext</c>, not <c>IdentityDbContext</c> — that name collides with the
/// ASP.NET base type <see cref="Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext{TUser,TRole,TKey}"/>
/// itself. It compiles fine with qualification at the base-type position, but costs every future
/// reader a double-take.
///
/// Uses the 8-generic-parameter form, not the 3-parameter shorthand PLAN.md's text shows — the
/// shorthand would silently fall back to the stock <see cref="IdentityRoleClaim{TKey}"/> instead of
/// <see cref="ApplicationRoleClaim"/>, orphaning the Group/DisplayName/Description fields G1-02
/// ported it specifically to carry.
/// </summary>
internal sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options)
    : IdentityDbContext<
        ApplicationUser,
        ApplicationRole,
        string,
        IdentityUserClaim<string>,
        IdentityUserRole<string>,
        IdentityUserLogin<string>,
        ApplicationRoleClaim,
        IdentityUserToken<string>>(options)
{
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<SeedMarker> SeedMarkers => Set<SeedMarker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityModuleDbContext).Assembly);
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}
