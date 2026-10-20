using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel;

namespace Identity.Infrastructure.Services;

internal sealed class UserProvisioning(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ILogger<UserProvisioning> logger) : IUserProvisioning
{
    /// <summary>ASP.NET Identity's unique index on <c>NormalizedUserName</c>.</summary>
    internal const string UserNameIndex = "UserNameIndex";

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

        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UserNameIndex,
        })
        {
            // Aug 3.5: a concurrent provisioning of the same email won between the check above and this
            // insert. UserName is the email, so ASP.NET Identity's unique UserNameIndex is what catches it
            // (EmailIndex is not unique). Same answer as the check. Return at once: nothing else may run
            // on the failed statement. This is Identity's own context, so a caller's transaction in
            // another module is not affected.
            return UserErrors.DuplicatedEmail(request.Email);
        }

        // Aug 3.5: the other half of the same race. When the concurrent provisioning has already
        // committed, Identity's own UserValidator sees it before the insert and refuses with
        // DuplicateUserName; that is still "this email is taken", not the vague CreateFailed below.
        if (createResult.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName)
                                         or nameof(IdentityErrorDescriber.DuplicateEmail)))
        {
            return UserErrors.DuplicatedEmail(request.Email);
        }

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
