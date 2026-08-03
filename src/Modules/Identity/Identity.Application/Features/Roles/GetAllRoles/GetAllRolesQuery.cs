using BuildingBlocks.Messaging;
using Identity.Contracts.Roles;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.GetAllRoles;

internal sealed class GetAllRolesQueryHandler(IRoleService roleService)
    : IQueryHandler<GetAllRolesQuery, IReadOnlyList<RoleResponse>>
{
    public async Task<Result<IReadOnlyList<RoleResponse>>> HandleAsync(
        GetAllRolesQuery query, CancellationToken ct = default)
    {
        var roles = await roleService.GetAllRolesAsync(ct);
        return Result.Success<IReadOnlyList<RoleResponse>>([.. roles.Select(r => r.ToResponse())]);
    }
}
