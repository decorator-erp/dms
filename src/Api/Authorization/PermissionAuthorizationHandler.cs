using BuildingBlocks;
using Microsoft.AspNetCore.Authorization;

namespace Api.Authorization;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Services/Authentication/Filters/PermissionAutherizationHandler.cs</c>
/// (name typo fixed on the way in — this is a new class in this solution, not a hand-edit of ported
/// code, so there's no reason to carry the typo forward). Succeeds iff the caller is authenticated and
/// carries a matching permission claim; never calls <c>Fail()</c>, so it never blocks a policy another
/// handler could still satisfy.
/// </summary>
internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<HasPermissionAttribute>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, HasPermissionAttribute requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(PermissionClaim.Type, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
