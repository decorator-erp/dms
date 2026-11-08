using BuildingBlocks;
using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Web.Authorization;

/// <summary>The <see cref="HasAnyPermissionAttribute"/> counterpart of
/// <see cref="PermissionAuthorizationHandler"/>: succeeds when the authenticated caller carries a
/// permission claim for at least one of the listed codes, and never calls <c>Fail()</c>. P1-A: with none of them in
/// the token, it falls back to the codes the caller's active project access grants imply, as
/// <see cref="PermissionAuthorizationHandler"/> does.</summary>
public sealed class AnyPermissionAuthorizationHandler : AuthorizationHandler<HasAnyPermissionAttribute>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, HasAnyPermissionAttribute requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (requirement.Permissions.Any(p => context.User.HasClaim(PermissionClaim.Type, p))
            || await ImpliedPermissionFallback.HoldsAsync(context, requirement.Permissions))
        {
            context.Succeed(requirement);
        }
    }
}
