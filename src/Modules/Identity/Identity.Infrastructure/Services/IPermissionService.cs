using Identity.Domain;
using SharedKernel;

namespace Identity.Infrastructure.Services;

/// <summary>Returns <c>ApplicationRoleClaim</c> entities directly rather than an intermediate DTO —
/// the entity already carries <c>Group</c>/<c>DisplayName</c>/<c>Description</c> (G1-02), so
/// Identity.Application's <c>ToResponse()</c> extensions (G1-06) can map straight from it. The user and
/// all-permissions lists return one row per permission code (Aug 3.3).</summary>
internal interface IPermissionService
{
    Task<IReadOnlyList<ApplicationRoleClaim>> GetUserPermissionsAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<ApplicationRoleClaim>> GetAllPermissionsAsync(CancellationToken ct = default);

    Task<Result> AssignPermissionToRoleAsync(string roleId, string permission, CancellationToken ct = default);

    Task<Result> RemovePermissionFromRoleAsync(string roleId, string permission, CancellationToken ct = default);

    Task<IReadOnlyList<ApplicationRoleClaim>> GetRolePermissionsAsync(string roleId, CancellationToken ct = default);
}
