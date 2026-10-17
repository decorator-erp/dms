using BuildingBlocks.Messaging;
using Identity.Contracts.Roles;
using Identity.Domain;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.GetRole;

internal sealed class GetRoleQueryHandler(IRoleService roleService) : IQueryHandler<GetRoleQuery, RoleResponse>
{
    public async Task<Result<RoleResponse>> HandleAsync(GetRoleQuery query, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleService.GetByIdAsync(query.RoleId, ct);
        return role is null ? RoleErrors.NotFound : role.ToResponse();
    }
}
