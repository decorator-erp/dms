using BuildingBlocks.Messaging;
using Identity.Application.DTOs;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetRolePermissions;

internal sealed record GetRolePermissionsQuery(string RoleId) : IQuery<IReadOnlyList<PermissionResponse>>;

internal sealed class GetRolePermissionsQueryHandler(IPermissionService permissionService)
    : IQueryHandler<GetRolePermissionsQuery, IReadOnlyList<PermissionResponse>>
{
    public async Task<Result<IReadOnlyList<PermissionResponse>>> HandleAsync(
        GetRolePermissionsQuery query, CancellationToken ct = default)
    {
        var claims = await permissionService.GetRolePermissionsAsync(query.RoleId, ct);
        return Result.Success<IReadOnlyList<PermissionResponse>>([.. claims.Select(c => c.ToResponse())]);
    }
}
