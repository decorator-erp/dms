using BuildingBlocks.Messaging;
using Identity.Contracts.Permissions;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetUserEffectivePermissions;

internal sealed class GetUserEffectivePermissionsQueryHandler(
    IEffectivePermissionService effectivePermissionService, IdentityModuleDbContext db)
    : IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse>
{
    public async Task<Result<EffectivePermissionsResponse>> HandleAsync(
        GetUserEffectivePermissionsQuery query, CancellationToken ct = default)
    {
        IEnumerable<string> effectivePermissions = await effectivePermissionService.ResolveAsync(query.UserId, ct);

        List<PermissionOverrideResponse> overrides = await db.UserPermissionOverrides
            .AsNoTracking()
            .Where(o => o.UserId == query.UserId)
            .Select(o => new PermissionOverrideResponse(
                o.Id, o.Permission, o.IsGranted, o.GrantedById, o.Reason, o.ExpiresAt))
            .ToListAsync(ct);

        return new EffectivePermissionsResponse(query.UserId, [.. effectivePermissions], overrides);
    }
}
