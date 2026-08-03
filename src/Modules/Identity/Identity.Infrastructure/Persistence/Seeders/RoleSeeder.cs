using Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Persistence.Seeders;

/// <summary>Ported from <c>../backend/Infrastructure/Persistence/Seeders/RoleSeeder.cs</c>. Idempotent
/// by construction: skips any role name that already exists, so re-running on a populated database adds
/// nothing.</summary>
internal static class RoleSeeder
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager, CancellationToken ct = default)
    {
        foreach (var roleName in DefaultRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));
        }
    }
}
