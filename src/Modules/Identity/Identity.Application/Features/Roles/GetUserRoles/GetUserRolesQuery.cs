using BuildingBlocks.Messaging;
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
/// </summary>
internal sealed class GetUserRolesQueryHandler(IRoleService roleService)
    : IQueryHandler<GetUserRolesQuery, IReadOnlyList<RoleResponse>>
{
    public async Task<Result<IReadOnlyList<RoleResponse>>> HandleAsync(
        GetUserRolesQuery query, CancellationToken ct = default)
    {
        IReadOnlyList<ApplicationRole> roles = await roleService.GetUserRolesAsync(query.UserId, ct);
        return Result.Success<IReadOnlyList<RoleResponse>>([.. roles.Select(r => r.ToResponse())]);
    }
}
