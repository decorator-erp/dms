using BuildingBlocks.Messaging;
using Identity.Contracts.Permissions;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetUserEffectivePermissions;

/// <summary>Ported from <c>../backend/Application/Features/Permissions/Queries/GetEffective</c>. Unlike
/// <see cref="Identity.Application.Features.Permissions.GetUserPermissions.GetUserPermissionsQueryHandler"/>,
/// this includes per-user overrides on top of role-derived permissions — the actual
/// authorization-relevant set, via <see cref="IEffectivePermissionService"/> (the same service the JWT
/// issuer uses).</summary>
internal sealed class GetUserEffectivePermissionsQueryHandler(
    IEffectivePermissionService effectivePermissionService, IdentityModuleDbContext db)
    : IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse>
{
    public async Task<Result<EffectivePermissionsResponse>> HandleAsync(
        GetUserEffectivePermissionsQuery query, CancellationToken ct = default)
    {
        var effectivePermissions = await effectivePermissionService.ResolveAsync(query.UserId, ct);

        var overrides = await db.UserPermissionOverrides
            .AsNoTracking()
            .Where(o => o.UserId == query.UserId)
            .Select(o => new PermissionOverrideResponse(
                o.Id, o.Permission, o.IsGranted, o.GrantedById, o.Reason, o.ExpiresAt))
            .ToListAsync(ct);

        return new EffectivePermissionsResponse(query.UserId, [.. effectivePermissions], overrides);
    }
}
