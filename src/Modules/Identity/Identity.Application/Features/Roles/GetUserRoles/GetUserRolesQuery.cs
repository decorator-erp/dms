using BuildingBlocks;
using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Roles;
using Identity.Domain;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.GetUserRoles;

/// <summary>
/// A thin passthrough, like every other Role handler. <see cref="IRoleService.GetUserRolesAsync"/>
/// returns an empty list both for a user that holds no roles and for a user id that matches nobody,
/// and cannot tell the two apart — so this answers <c>200 []</c> in either case rather than inventing
/// a 404 the service cannot substantiate. Callers: an empty array is not proof the user exists.
///
/// <para><b>Who may ask (N6).</b> Anyone may read their own roles; reading someone else's needs
/// <c>roles.read</c>. The route is authenticated-only, because "self" is not something a route claim
/// can express, so the rule lives here. Anyone else gets the same 404 an unknown id would, which tells
/// them nothing about whether the account exists.</para>
/// </summary>
internal sealed class GetUserRolesQueryHandler(IRoleService roleService, ICurrentUser currentUser)
    : IQueryHandler<GetUserRolesQuery, IReadOnlyList<RoleResponse>>
{
    public async Task<Result<IReadOnlyList<RoleResponse>>> HandleAsync(
        GetUserRolesQuery query, CancellationToken ct = default)
    {
        if (query.UserId != currentUser.UserId && !currentUser.HasPermission(IdentityPermissions.RolesRead))
        {
            return UserErrors.NotFound;
        }

        IReadOnlyList<ApplicationRole> roles = await roleService.GetUserRolesAsync(query.UserId, ct);
        return Result.Success<IReadOnlyList<RoleResponse>>([.. roles.Select(r => r.ToResponse())]);
    }
}
