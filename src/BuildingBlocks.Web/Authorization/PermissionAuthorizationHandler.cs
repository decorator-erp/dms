using BuildingBlocks;
using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Web.Authorization;

/// <summary>
/// Moved here from <c>Api/Authorization</c> at G1-07, alongside <see cref="HasPermissionAttribute"/> —
/// registered once in <c>Program.cs</c> as a singleton <c>IAuthorizationHandler</c>, same as before the
/// move. Ported originally from
/// <c>../backend/Infrastructure/Services/Authentication/Filters/PermissionAutherizationHandler.cs</c>
/// (name typo fixed on the way in — this is a new class in this solution, not a hand-edit of ported
/// code, so there's no reason to carry the typo forward). Succeeds iff the caller is authenticated and
/// carries a matching permission claim; never calls <c>Fail()</c>, so it never blocks a policy another
/// handler could still satisfy.
///
/// <para>P1-A: when the claim is missing it falls back to the codes the caller's active project access grants
/// imply (<see cref="ImpliedPermissionFallback"/>), read live, so a grant or its revocation counts on the next
/// request with no new token.</para>
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<HasPermissionAttribute>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, HasPermissionAttribute requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (context.User.HasClaim(PermissionClaim.Type, requirement.Permission)
            || await ImpliedPermissionFallback.HoldsAsync(context, [requirement.Permission]))
        {
            context.Succeed(requirement);
        }
    }
}
