using System.Text;
using System.Threading.RateLimiting;
using BuildingBlocks;
using BuildingBlocks.Auditing;
using BuildingBlocks.Jobs;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

        var jwtKey = cfg["JwtSettings:Key"];
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
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken)
                            && context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddOpenApi();

        services.AddCors(corsOptions =>
        {
            var allowedOrigins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        // SharedDbContext + AddOutbox() were never wired into the composition root before this - every
        // module's outbox writes (UserRegisteredIntegrationEvent, EmployeeHiredIntegrationEvent, etc.) landed
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

        return services;
    }
}
