using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Api.Jobs;
using BuildingBlocks;
using BuildingBlocks.Auditing;
using BuildingBlocks.Jobs;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using Identity.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

namespace Api;

public static class DI
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        // N1-02. Singleton to match AuditSaveChangesInterceptor, which consumes it - see
        // ModuleDbContextRegistration.AddModuleDbContext for why that lifetime is load-bearing.
        // Registration ORDER matters: Program.cs runs AddApi before the module loop, so this wins over
        // the TryAdd'd NullAuditContextAccessor every module registers. Reverse the two and audit
        // silently loses every actor, which is why AuditContextAccessorRegistrationTests asserts the
        // real host resolves this one.
        services.AddSingleton<IAuditContextAccessor, HttpAuditContextAccessor>();

        string? jwtKey = cfg["JwtSettings:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException(
                "JwtSettings:Key is not configured. Set it with "
                + "`dotnet user-secrets set \"JwtSettings:Key\" \"<a long random value>\" --project src/Api` "
                + "for local development, or via configured environment variables elsewhere. There is no "
                + "default — a committed signing key is a credential in the repository.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = cfg["JwtSettings:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = cfg["JwtSettings:Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,

                    // Aug 5.1: pinned, as in JwtProvider.ValidateToken. The token's header must not pick
                    // the algorithm it is verified with.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                };

                // N1-04: SignalR's browser client cannot set an Authorization header on a WebSocket
                // handshake, so it appends the token as ?access_token=. Without this, every real client
                // gets 401 from /hubs/notifications and the hub is decorative.
                //
                // SCOPED TO /hubs, deliberately. A blanket query-string token reader would accept
                // credentials on every route in the app - and query strings land in access logs, proxy
                // logs, Referer headers and browser history, none of which are places a bearer token
                // should be. The path check is what keeps this to the one transport that needs it.
                //
                // Host-level, not module-level: the module has no access to the JwtBearer options, and
                // authentication is the composition root's job.
                options.Events = new JwtBearerEvents
                {
                    // N2: a validly signed token is not enough, it must also be current. Its sstamp claim
                    // must equal the user's stored security stamp, and the user must exist and be
                    // active. Identity bumps the stamp whenever a token's contents would change
                    // (deactivation, roles, role claims, overrides, password, logout), so this refuses
                    // tokens issued before the change. IUserSessionState caches briefly
                    // (Identity:SessionStateCacheSeconds) and is evicted on every bump.
                    OnTokenValidated = async context =>
                    {
                        string? userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                        string? stamp = context.Principal?.FindFirst(SessionClaims.SecurityStamp)?.Value;

                        UserSessionSnapshot? session = userId is null
                            ? null
                            : await context.HttpContext.RequestServices.GetRequiredService<IUserSessionState>()
                                .GetAsync(userId, context.HttpContext.RequestAborted);

                        if (session is null || !session.IsActive || stamp is null || session.SecurityStamp != stamp)
                        {
                            context.HttpContext.Items[SessionClaims.StaleTokenItem] = true;
                            context.Fail(SessionClaims.StaleTokenCode);
                        }
                    },

                    // A stale token gets a 401 that says so, with the same problem shape as every other
                    // error, so the client knows to refresh (or sign in) rather than guess.
                    OnChallenge = async context =>
                    {
                        if (context.HttpContext.Items.ContainsKey(SessionClaims.StaleTokenItem))
                        {
                            context.HandleResponse();
                            await Results.Problem(
                                    statusCode: StatusCodes.Status401Unauthorized,
                                    title: SessionClaims.StaleTokenCode,
                                    detail: "This session is no longer valid. Sign in again.",
                                    extensions: new Dictionary<string, object?>
                                    {
                                        ["errors"] = new Dictionary<string, object?>
                                        {
                                            [SessionClaims.StaleTokenCode] = new List<string> { "This session is no longer valid. Sign in again." },
                                        },
                                    })
                                .ExecuteAsync(context.HttpContext);
                        }
                    },

                    OnMessageReceived = context =>
                    {
                        StringValues accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken)
                            && context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        // K7: deny by default. An endpoint that declares no authorization at all (a new route someone forgot
        // to gate) now requires an authenticated user instead of being silently anonymous. Anonymous routes
        // say .AllowAnonymous() explicitly, and Architecture.Tests' Rule 12 checks both directions: every
        // route is authorized or on the anonymous list, and every authorized route names a permission or
        // is on the authenticated-only list.
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddOpenApi(options =>
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>()
        );

        services.AddCors(corsOptions =>
        {
            string[] allowedOrigins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            corsOptions.AddPolicy(CorsPolicies.Default, policy =>
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
        });

        services.AddHealthChecks();

        // G9-14: the repo's first rate limiter, and (per PLAN-PHASE4.md's own correction to the phase
        // brief) a shared-host concern, not a module-local one - no rate limiter existed anywhere before
        // this. Fixed-window, keyed by remote IP (not by user - the whole point of this policy is that
        // the caller is anonymous), for CRM's public lead-intake endpoint. Five submissions per minute
        // per IP is deliberately tight: a real prospect submits once, a script hammering the endpoint
        // does not get a second window before being throttled. QueueLimit 0 - a caller over the limit
        // is rejected immediately (429), never made to wait.
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.CrmPublicLeads, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Phase 1B: the auth routes (login, refresh, revoke, change-password, logout). Same shape as
            // the public-lead policy: fixed window, keyed by remote IP, rejected at once (no queue). Ten
            // a minute is several honest retries and far too few for guessing. Configurable so the
            // integration suite (one host, one IP, hundreds of logins) can raise it, and so one test can
            // lower it to assert the 429.
            int authPermitLimit = cfg.GetValue("RateLimiting:AuthLogin:PermitLimit", 10);
            int authWindowSeconds = cfg.GetValue("RateLimiting:AuthLogin:WindowSeconds", 60);
            options.AddPolicy(RateLimitPolicies.AuthLogin, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPermitLimit,
                        Window = TimeSpan.FromSeconds(authWindowSeconds),
                        QueueLimit = 0,
                    }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        // SharedDbContext + AddOutbox() were never wired into the composition root before this - every
        // module's outbox writes (EmployeeHiredIntegrationEvent, ClientCreatedIntegrationEvent, etc.) landed
        // in shared.outbox_messages successfully (each module maps that table on its own DbContext), but
        // nothing was ever registered to poll and publish them. Found while wiring G2-04's deactivate -> outbox
        // -> Identity flow, which is the first feature that actually depends on a message getting delivered.
        services.AddDbContext<SharedDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));
        services.AddOutbox();

        // N1-01: the second (and last) always-on loop in the process. Registers the worker only - each
        // module registers its own IPeriodicJob implementations in its own Register, so with none
        // registered yet this is a clean no-op. Nine backgrounded loops in the old backend become one
        // here; see docs/low-resource-hosting.md §3.
        services.AddPeriodicJobs();

        // The one job the Api owns. It reads Identity, HR and CRM contracts together to decide whether a
        // provisioned login was ever claimed, which no single module may do - Identity is a sink and
        // cannot see the other two. See UnlinkedUserReaperJob's own remarks, and
        // IdentityContractsIsolationTests for the boundary this placement respects.
        services.AddScoped<IPeriodicJob, UnlinkedUserReaperJob>();

        return services;
    }
}

/// <summary>N2's names, shared by the token check above. <c>sstamp</c> must match
/// <c>JwtProvider.SecurityStampClaim</c> in Identity, which Api cannot reference (it is internal).</summary>
internal static class SessionClaims
{
    public const string SecurityStamp = "sstamp";
    public const string StaleTokenCode = AuthErrorCodes.StaleToken;
    public const string StaleTokenItem = "auth:stale-token";
}
