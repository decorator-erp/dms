using BuildingBlocks;
using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Web.Authorization;

/// <summary>The <see cref="HasAnyPermissionAttribute"/> counterpart of
/// <see cref="PermissionAuthorizationHandler"/>: succeeds when the authenticated caller carries a
/// permission claim for at least one of the listed codes, and never calls <c>Fail()</c>.</summary>
public sealed class AnyPermissionAuthorizationHandler : AuthorizationHandler<HasAnyPermissionAttribute>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, HasAnyPermissionAttribute requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && requirement.Permissions.Any(p => context.User.HasClaim(PermissionClaim.Type, p)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
