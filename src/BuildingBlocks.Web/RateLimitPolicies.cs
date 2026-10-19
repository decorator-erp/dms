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

    /// <summary>HR/Identity Phase 1B: <c>/api/auth/login</c>, <c>/refresh</c>, <c>/revoke</c>,
    /// <c>/change-password</c> and <c>/logout</c>. One fixed window, keyed by remote IP and shared across
    /// all five routes, so a guesser cannot spread attempts across them. Lockout (K4) stops guessing one
    /// account; this stops one client trying many.</summary>
    public const string AuthLogin = "auth-login";
}
