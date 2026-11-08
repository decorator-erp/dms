using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web.Authorization;

/// <summary>
/// P1-A: the shared fallback of <see cref="PermissionAuthorizationHandler"/> and
/// <see cref="AnyPermissionAuthorizationHandler"/>. When the token lacks every code a requirement names, ask the
/// request's <see cref="ImpliedPermissions"/> (project access grants, read live). The handlers are singletons, so the
/// scoped service comes from the request's own services; a host that registers no <see cref="ImpliedPermissions"/>
/// (or a non-HTTP authorization call) simply gets no fallback.
/// </summary>
internal static class ImpliedPermissionFallback
{
    public static async Task<bool> HoldsAsync(AuthorizationHandlerContext context, IEnumerable<string> permissions)
    {
        if (context.Resource is not HttpContext http
            || http.RequestServices.GetService<ImpliedPermissions>() is not { } implied
            || context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value is not { Length: > 0 } userId)
        {
            return false;
        }

        return await implied.TryAuthorizeAsync(userId, permissions, http.RequestAborted);
    }
}
