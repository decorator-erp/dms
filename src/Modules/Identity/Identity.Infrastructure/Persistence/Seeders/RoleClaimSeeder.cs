using BuildingBlocks;
using Identity.Contracts;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Seeders;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Seeders/RoleClaimSeeder.cs</c>: for each of
/// <see cref="DefaultRoles.FullAccessRoles"/>, add whatever <see cref="IPermissionCatalog"/> strings the
/// role doesn't already carry — the backend's own "diff additively" idempotency, computed here via
/// <c>Except</c> against the role's existing claim values rather than re-adding (and duplicating) ones
/// already present. Reads the catalog, never a hard-coded permission list, so this widens automatically
/// once HR (or any later module) registers its own <c>IModulePermissions</c>.
///
/// <para><b>It stays additive on purpose (N3).</b> A full-access role means "every permission the
/// catalog knows, today and after the next module ships". So the seeder must keep adding new codes,
/// and it never removes one. That used to clash with the admin API: a claim removed from
/// <c>manager</c> came back silently at the next boot. Since N3, <c>PermissionService</c> refuses every
/// claim edit on a full-access role (409 <c>roles.FullAccessLocked</c>), which makes this seeder the
/// only writer of those roles' claims. Additive is then simply correct.</para>
/// </summary>
internal static class RoleClaimSeeder
{
    public static async Task SeedAsync(
        RoleManager<ApplicationRole> roleManager,
        IdentityModuleDbContext db,
        IPermissionCatalog catalog,
        CancellationToken ct = default)
    {
        string[] allPermissions = catalog.All().SelectMany(m => m.Permissions).Distinct().ToArray();
        if (allPermissions.Length == 0)
        {
            return;
        }

        foreach (string roleName in DefaultRoles.FullAccessRoles)
        {
            ApplicationRole? role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            List<string> existing = await db.RoleClaims
                .Where(rc => rc.RoleId == role.Id && rc.ClaimType == PermissionClaim.Type)
                .Select(rc => rc.ClaimValue!)
                .ToListAsync(ct);

            string[] missing = allPermissions.Except(existing).ToArray();
            if (missing.Length == 0)
            {
                continue;
            }

            foreach (string? permission in missing)
            {
                db.RoleClaims.Add(ApplicationRoleClaim.Create(role.Id, PermissionClaim.Type, permission));
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
