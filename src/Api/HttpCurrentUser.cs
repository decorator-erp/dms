using System.Security.Claims;
using BuildingBlocks;

namespace Api;

/// <summary>The only place <see cref="ICurrentUser"/> touches <see cref="HttpContext"/> — modules
/// never see it.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public bool HasPermission(string permission) =>
        accessor.HttpContext?.User.HasClaim(PermissionClaim.Type, permission) ?? false;
}
