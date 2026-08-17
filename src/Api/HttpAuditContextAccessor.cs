using System.Security.Claims;
using BuildingBlocks.Auditing;

namespace Api;

/// <summary>
/// The HTTP-backed <see cref="IAuditContextAccessor"/>. Lives here rather than in BuildingBlocks for
/// the same reason <see cref="HttpCurrentUser"/> does — modules never see <c>HttpContext</c>, and
/// architecture rule 8b makes it structural rather than stylistic: BuildingBlocks.csproj is text-scanned
/// for "AspNetCore", so it could not reference this even if the design allowed it.
///
/// <para><b>Registered as a singleton</b>, matching the interceptor that consumes it — see
/// <c>ModuleDbContextRegistration.AddModuleDbContext</c> for why that lifetime is load-bearing. Safe,
/// because it holds no state: <see cref="IHttpContextAccessor"/> is itself a singleton over an
/// <c>AsyncLocal</c>.</para>
///
/// <para><b>A null <c>HttpContext</c> yields <see cref="AuditContext.None"/>, not an exception.</b>
/// Three real paths resolve this outside a request — <c>IModule.SeedAsync</c> at startup, the outbox
/// processor's per-message scope, and <c>PeriodicJobWorker</c>'s per-job scope. Those writes are still
/// audited, with a null actor: the row keeps what changed and when, and loses only who. Refusing to
/// write it would put the hole exactly where the trail matters most.</para>
/// </summary>
internal sealed class HttpAuditContextAccessor(IHttpContextAccessor httpContextAccessor) : IAuditContextAccessor
{
    public AuditContext GetCurrent()
    {
        if (httpContextAccessor.HttpContext is not { } context)
            return AuditContext.None;

        var user = context.User;

        return new AuditContext(
            UserId: user.FindFirstValue(ClaimTypes.NameIdentifier),
            UserEmail: user.FindFirstValue(ClaimTypes.Email),
            // RemoteIpAddress first: X-Forwarded-For is caller-supplied and trivially spoofed, so it is
            // only consulted when there is no connection address at all.
            IpAddress: context.Connection.RemoteIpAddress?.ToString()
                ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault(),
            UserAgent: context.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null,
            RequestPath: context.Request.Path.Value,
            RequestMethod: context.Request.Method);
    }
}
