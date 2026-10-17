using Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Persistence.Seeders;

internal static class RoleSeeder
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (string roleName in DefaultRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));
        }
    }
}
