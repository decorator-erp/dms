using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
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
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Api;

public static class DI
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
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

        EnsureConnectionStringPassword(cfg, env);

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
            // PF-D2: Retry-After is exposed so a browser client can read it off a 429 and wait that long.
            corsOptions.AddPolicy(CorsPolicies.Default, policy =>
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
                    .WithExposedHeaders(HeaderNames.RetryAfter));
        });

        // PF-D2: the real client IP behind a reverse proxy, for the IP-keyed rate limits. Only proxies named
        // in configuration are trusted. The framework trusts loopback by default; that is cleared, so a
        // proxy on the same host must be listed as 127.0.0.1. With nothing configured, nothing is forwarded
        // at all: ForwardedHeadersMiddleware skips the known-proxy check when both lists are EMPTY and then
        // trusts every sender, which would let any client pick its own rate-limit bucket. ForwardLimit 1:
        // only the entry the trusted proxy itself appended is used.
        string[] knownProxies = cfg.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        string[] knownNetworks = cfg.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (string proxy in knownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (string network in knownNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }

            options.ForwardLimit = 1;
            options.ForwardedHeaders = options.KnownProxies.Count + options.KnownIPNetworks.Count == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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

            // The auth routes, three policies since PF-D2 (Phase 1B had one, shared by all five routes and
            // keyed by IP, so everyone behind one NAT shared one bucket). Each is a fixed window, rejected
            // at once (no queue), and configurable so the integration suite (one host, one IP, hundreds of
            // logins) can raise it and single tests can lower it to assert the 429.
            //
            // AuthLogin, /login only, per IP: ten a minute is several honest retries and far too few for
            // guessing.
            Dictionary<string, int> windowSeconds = new(StringComparer.Ordinal) { [RateLimitPolicies.CrmPublicLeads] = 60 };
            AddFixedWindow(options, cfg, windowSeconds, RateLimitPolicies.AuthLogin, "AuthLogin", 10, ClientIp);

            // AuthRefresh, /refresh and /revoke, per IP: a flood limit, not a guessing defence (refresh
            // tokens are random), sized for an office refreshing at once after a role edit. The
            // partitioner never reads the body.
            AddFixedWindow(options, cfg, windowSeconds, RateLimitPolicies.AuthRefresh, "AuthRefresh", 300, ClientIp);

            // AuthSession, /change-password and /logout, per signed-in user. Needs UseAuthentication
            // before UseRateLimiter (Program.cs). A request with no valid session (a stale token: the
            // stamp check leaves it anonymous) falls back to its IP.
            AddFixedWindow(options, cfg, windowSeconds, RateLimitPolicies.AuthSession, "AuthSession", 10, httpContext =>
                httpContext.User.Identity?.IsAuthenticated == true
                && (httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value) is { } userId
                    ? $"user:{userId}"
                    : ClientIp(httpContext));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // PF-D2: every 429 says when to come back. The limiter knows the time left in the window; the
            // window length is the fallback. Set once for all policies, so the public lead form gets it too.
            // The body stays empty.
            options.OnRejected = (context, _) =>
            {
                int seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter)
                    ? (int)Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                    : context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName is { } policy
                        && windowSeconds.TryGetValue(policy, out int window)
                        ? window
                        : 60;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
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

    private static string ClientIp(HttpContext httpContext) =>
        $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static void AddFixedWindow(
        RateLimiterOptions options, IConfiguration cfg, Dictionary<string, int> windowSeconds,
        string policyName, string configName, int defaultPermitLimit, Func<HttpContext, string> partitionKey)
    {
        int permitLimit = cfg.GetValue($"RateLimiting:{configName}:PermitLimit", defaultPermitLimit);
        int seconds = cfg.GetValue($"RateLimiting:{configName}:WindowSeconds", 60);
        windowSeconds[policyName] = seconds;
        options.AddPolicy(policyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionKey(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(seconds),
                    QueueLimit = 0,
                }));
    }

    /// <summary>FINDING-P1-9: <c>appsettings.json</c> commits the connection string with an empty password (L-6),
    /// so outside Development the environment must supply the whole string. A host that would otherwise start
    /// and fail on its first query instead refuses to start, naming the variable — never echoing the value,
    /// which may hold the rest of the credentials. Development keeps its own password in
    /// <c>appsettings.Development.json</c>; integration hosts run as Development with the Testcontainers
    /// string.</summary>
    private static void EnsureConnectionStringPassword(IConfiguration cfg, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            return;
        }

        string? problem = ConnectionStringProblem(cfg.GetConnectionString("DefaultConnection"), Environment.GetEnvironmentVariable);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }
    }

    /// <summary>FINDING-P1-13: <see langword="null"/> when <paramref name="connectionString"/> can authenticate,
    /// else the reason it cannot — naming <c>ConnectionStrings__DefaultConnection</c>, never a value. An empty
    /// password is fine when Npgsql has another way in: a passfile, a client certificate, Kerberos/GSS (Npgsql
    /// 10 has no <c>Integrated Security</c> keyword — it rejects it — so a GSS deployment says so by naming
    /// <c>Kerberos Service Name</c> or <c>Include Realm</c>), or <c>PGPASSWORD</c>/<c>PGPASSFILE</c> in the
    /// environment. <paramref name="environmentVariable"/> is only asked whether those are set; their values are
    /// never read into a message or a log.</summary>
    internal static string? ConnectionStringProblem(string? connectionString, Func<string, string?> environmentVariable)
    {
        const string Remedy = " Set the whole connection string through the ConnectionStrings__DefaultConnection "
            + "environment variable (or another configuration source); appsettings.json deliberately commits none.";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "ConnectionStrings:DefaultConnection is not configured outside Development." + Remedy;
        }

        Npgsql.NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException)
        {
            // Not ex.Message: Npgsql's parse errors can quote the offending keyword or value.
            return "ConnectionStrings:DefaultConnection could not be parsed outside Development. Check the "
                + "value of the ConnectionStrings__DefaultConnection environment variable (or other configuration "
                + "source) is a valid Npgsql connection string.";
        }

        bool authenticatesWithoutPassword =
            !string.IsNullOrEmpty(builder.Passfile)
            || !string.IsNullOrEmpty(builder.SslCertificate)
            || builder.Keys.Cast<string>().Any(key =>
                key.Equals("Kerberos Service Name", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Include Realm", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(environmentVariable("PGPASSWORD"))
            || !string.IsNullOrEmpty(environmentVariable("PGPASSFILE"));

        return !string.IsNullOrEmpty(builder.Password) || authenticatesWithoutPassword
            ? null
            : "ConnectionStrings:DefaultConnection has no password outside Development." + Remedy;
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
