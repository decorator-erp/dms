namespace BuildingBlocks.Web;

/// <summary>
/// Named rate-limit policies, registered once in <c>Api.DI.AddApi</c> and referenced by name from the
/// endpoint that needs one — same split as <c>CorsPolicies</c>/<c>Api.UseCors</c>, except this constant
/// lives here (not in <c>Api</c>) because, unlike CORS, a rate-limit policy is applied per-endpoint via
/// <c>.RequireRateLimiting(name)</c> from inside a module's own <c>Crm.Infrastructure</c>-style project,
/// which has no reference to <c>Api</c> and shouldn't gain one just for a string constant.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>G9-14: the anonymous <c>POST /api/crm/public/leads</c> intake — fixed-window, IP-keyed
    /// (see <c>Api.DI.AddApi</c>'s registration for the window/limit values and the reasoning for
    /// picking a fixed window over a sliding one).</summary>
    public const string CrmPublicLeads = "crm-public-leads";

    /// <summary>HR/Identity Phase 1B, narrowed by PF-D2 to <c>/api/auth/login</c> only: a strict fixed
    /// window keyed by client IP. Lockout (K4) stops guessing one account; this stops one client trying
    /// many.</summary>
    public const string AuthLogin = "auth-login";

    /// <summary>PF-D2: <c>/api/auth/refresh</c> and <c>/revoke</c>. Keyed by client IP, with a generous
    /// default: refresh tokens are random and unguessable, so this is a flood limit, not a guessing
    /// defence, and a whole office behind one NAT refreshing at once must fit.</summary>
    public const string AuthRefresh = "auth-refresh";

    /// <summary>PF-D2: <c>/api/auth/change-password</c> and <c>/logout</c>. Keyed by the signed-in user, so
    /// colleagues behind one IP do not share a bucket; by client IP when the request carries no valid
    /// session.</summary>
    public const string AuthSession = "auth-session";
}
