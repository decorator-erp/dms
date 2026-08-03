using Identity.Domain;
using SharedKernel;

namespace Identity.Infrastructure.Services;

internal interface IRoleService
{
    Task<Result<string>> CreateRoleAsync(string roleName, CancellationToken ct = default);
    Task<Result> UpdateRoleAsync(string roleId, string newRoleName, CancellationToken ct = default);
    Task<Result> DeleteRoleAsync(string roleId, CancellationToken ct = default);
    Task<Result> AssignRoleToUserAsync(string userId, string roleId, CancellationToken ct = default);
    Task<Result> RemoveRoleFromUserAsync(string userId, string roleId, CancellationToken ct = default);
    Task<bool> UserInRoleAsync(string userId, string roleId, CancellationToken ct = default);
    Task<bool> UserIsAdminAsync(string userId, CancellationToken ct = default);
    Task<ApplicationRole?> GetByIdAsync(string roleId, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationRole>> GetUserRolesAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationRole>> GetAllRolesAsync(CancellationToken ct = default);
}
