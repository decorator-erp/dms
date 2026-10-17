using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Identity.Infrastructure.Services;

internal sealed class UserProvisioning(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ILogger<UserProvisioning> logger) : IUserProvisioning
{
    public async Task<Result<string>> CreateAsync(NewUserRequest request, CancellationToken ct)
    {
        ApplicationRole? role = await roleManager.FindByNameAsync(request.RoleName);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (DefaultRoles.FullAccessRoles.Contains(role.Name, StringComparer.OrdinalIgnoreCase))
        {
            return UserErrors.RoleNotAssignableAtProvisioning;
        }

        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return UserErrors.DuplicatedEmail(request.Email);
        }

        var user = ApplicationUser.Create(
            Guid.NewGuid().ToString(),
            request.Email,
            request.Email,
            request.FirstName,
            request.LastName,
            request.Kind is UserKind.Client ? UserType.Client : UserType.Employee,
            request.PhoneNumber,
            isActive: false);

        IdentityResult createResult = await userManager.CreateAsync(user, request.Password);

        if (!createResult.Succeeded)
        {
            logger.LogWarning(
                "Provisioning failed to create a user: {IdentityErrors}",
                string.Join("; ", createResult.Errors.Select(e => e.Description)));

            return UserErrors.CreateFailed;
        }

        IdentityResult roleResult = await userManager.AddToRoleAsync(user, role.Name!);
        if (!roleResult.Succeeded)
        {
            logger.LogWarning(
                "Provisioning failed to assign role '{Role}'; deleting the user it had just created: {IdentityErrors}",
                role.Name,
                string.Join("; ", roleResult.Errors.Select(e => e.Description)));

            await userManager.DeleteAsync(user);
            return UserErrors.RoleAssignmentFailed;
        }

        return user.Id;
    }
}
