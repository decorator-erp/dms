using BuildingBlocks;
using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetUserEffectivePermissions;

/// <summary>
/// <b>Who may ask (PF-D1, the N6 pattern of <c>GetUserRolesQueryHandler</c>).</b> Anyone may read their
/// own effective set; reading someone else's needs <c>permissions.manage</c>. "Self" is not something a
/// route claim can express, so the rule lives here and both routes (<c>/users/{userId}/effective</c> and
/// <c>/me/effective</c>) are authenticated-only. Anyone else gets the same 404 an unknown id would.
/// </summary>
internal sealed class GetUserEffectivePermissionsQueryHandler(
    IEffectivePermissionService effectivePermissionService, IdentityModuleDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse>
{
    public async Task<Result<EffectivePermissionsResponse>> HandleAsync(
        GetUserEffectivePermissionsQuery query, CancellationToken ct = default)
    {
        if (query.UserId != currentUser.UserId && !currentUser.HasPermission(IdentityPermissions.PermissionsManage))
        {
            return UserErrors.NotFound;
        }

        IEnumerable<string> effectivePermissions = await effectivePermissionService.ResolveAsync(query.UserId, ct);

        List<PermissionOverrideResponse> overrides = await db.UserPermissionOverrides
            .AsNoTracking()
            .Where(o => o.UserId == query.UserId)
            // Aug 2.4: an expired override no longer affects anything, so it isn't listed either.
            .Where(UserPermissionOverride.LiveAt(DateTime.UtcNow))
            .Select(o => new PermissionOverrideResponse(
                o.Id, o.Permission, o.IsGranted, o.GrantedById, o.Reason, o.ExpiresAt))
            .ToListAsync(ct);

        return new EffectivePermissionsResponse(query.UserId, [.. effectivePermissions], overrides);
    }
}
