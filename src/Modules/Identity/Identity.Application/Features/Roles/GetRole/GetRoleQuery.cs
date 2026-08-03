using BuildingBlocks.Messaging;
using Identity.Application.DTOs;
using Identity.Domain;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.GetRole;

internal sealed record GetRoleQuery(string RoleId) : IQuery<RoleResponse>;

internal sealed class GetRoleQueryHandler(IRoleService roleService) : IQueryHandler<GetRoleQuery, RoleResponse>
{
    public async Task<Result<RoleResponse>> HandleAsync(GetRoleQuery query, CancellationToken ct = default)
    {
        var role = await roleService.GetByIdAsync(query.RoleId, ct);
        return role is null ? RoleErrors.NotFound : role.ToResponse();
    }
}
