using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 12 (K7): every HTTP route says who may call it. It is checked on the real host's endpoint data
/// sources: <c>WebApplicationFactory&lt;Program&gt;</c> builds exactly what <c>Program.cs</c> maps, module
/// routes and host routes alike, so a route cannot be missed by mapping it somewhere this test doesn't
/// look.
///
/// <list type="number">
/// <item><b>Tier 1, authorization.</b> Every route carries <see cref="IAuthorizeData"/>
/// (<c>RequireAuthorization</c>/<c>RequirePermission</c>/<c>[Authorize]</c>). The only exceptions are
/// <see cref="AnonymousRoutes"/>, which must carry <see cref="IAllowAnonymous"/>, so "anonymous" is a
/// decision written next to the route, not the absence of one. The host's <c>FallbackPolicy</c> (Api
/// <c>DI.cs</c>) protects an undeclared route at runtime anyway; this tier makes forgetting a red test
/// instead.</item>
/// <item><b>Tier 2, a named permission.</b> Every authorized route also carries
/// <see cref="HasPermissionAttribute"/> or, since N6, <see cref="HasAnyPermissionAttribute"/>, except
/// <see cref="AuthenticatedOnlyRoutes"/>, each with the reason it needs nothing beyond a signed-in
/// caller.</item>
/// <item><b>Every permission a route names is in the catalog</b> (N6). A misspelt code would gate the
/// route behind a permission nobody can ever be granted.</item>
/// </list>
/// Both lists are checked against the data source in reverse too: an entry whose route no longer exists
/// fails, so the lists cannot quietly outlive what they excuse.
/// </summary>
public sealed class Rule12EndpointAuthorizationTests : IAsyncLifetime
{
    /// <summary>Routes nobody signs in to reach. Each must say <c>.AllowAnonymous()</c>.</summary>
    private static readonly string[] AnonymousRoutes =
    [
        // The prompt's four (K7).
        "/api/auth/login", // obtaining a token is what an anonymous caller does
        "/api/auth/refresh", // accepts an EXPIRED access token by design; bearer auth would reject it first
        "/api/auth/revoke", // same: works with an expired access token
        "/api/crm/public/leads", // the public website's lead form; rate-limited and size-capped instead
        // Not in the prompt's list; found when the rule ran against the real host (recorded in the notes).
        "/health", // probed by load balancers and orchestrators without credentials
    ];

    /// <summary>Path prefixes that are anonymous: Development-only API documentation.</summary>
    private static readonly string[] AnonymousPrefixes =
    [
        "/openapi/", // MapOpenApi(), Development only
        "/scalar", // MapScalarApiReference(), Development only
    ];

    /// <summary>Authorized routes that need a signed-in caller and no particular permission.</summary>
    private static readonly string[] AuthenticatedOnlyRoutes =
    [
        // The prompt's three (K7).
        "/api/permissions/me/effective", // the caller's own permissions; the id comes from the token
        "/api/auth/change-password", // the caller's own password; needs the current one as well
        "/api/auth/logout", // ends the caller's own sessions; acts on nobody else
        // Found when the rule ran against the real host (recorded in the notes).
        "/api/me", // who-am-I: echoes the token's own user id
        "/hubs/notifications", // the caller's own notification stream; the hub pushes to their id only
        "/hubs/notifications/negotiate", // SignalR's handshake for the same hub
        // Either crm.briefs.view_any or the brief's own client may read it, so no single permission
        // fits the route; the handler decides with ICurrentUser.HasPermission plus ownership.
        "/api/crm/briefs/{briefId:guid}/rounds/{round:int}/diff",
        // N6: a user's roles, for themselves or for a roles.read holder. "Self" is not a route claim, so
        // GetUserRolesQueryHandler decides, answering 404 to anyone else.
        "/api/roles/users/{userId}",
    ];

    /// <summary>Entries that name a route a later phase adds; exempt from the "still exists" check.
    /// Empty since Phase 1B mapped <c>/api/auth/logout</c>.</summary>
    private static readonly string[] NotYetMapped = [];

    private PostgreSqlContainer _container = null!;
    private HostFactory _host = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // Program.cs runs every module's SeedAsync while building the host, so the schemas must exist
        // first, migrated from the same map Rule 9 uses.
        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        foreach (string schema in ModuleDependencyOrder.SchemaOrder)
        {
            if (Rule9CrossSchemaForeignKeyTests.Modules.TryGetValue(
                    schema, out (string AssemblyName, string ContextTypeName) module))
            {
                await Rule9CrossSchemaForeignKeyTests.MigrateModuleSchemaAsync(
                    _container.GetConnectionString(), module.AssemblyName, module.ContextTypeName);
            }
        }

        _host = new HostFactory(_container.GetConnectionString());
        _ = _host.Services;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public void EveryRoute_IsAuthorized_AndNamesAPermission_UnlessListed()
    {
        RouteEndpoint[] endpoints = [.. _host.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()];

        List<string> violations = [];
        HashSet<string> seen = [];
        HashSet<string> catalog = [.. _host.Services.GetRequiredService<Identity.Contracts.IPermissionCatalog>()
            .All().SelectMany(m => m.Permissions)];

        foreach (RouteEndpoint endpoint in endpoints)
        {
            string route = Normalize(endpoint.RoutePattern.RawText);
            string label = $"{Methods(endpoint)} {route}";
            seen.Add(route);

            bool allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            bool authorized = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            string[] named =
            [
                .. endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Select(a => a.Permission),
                .. endpoint.Metadata.GetOrderedMetadata<HasAnyPermissionAttribute>().SelectMany(a => a.Permissions),
            ];
            bool namesPermission = named.Length > 0;
            foreach (string unknown in named.Where(p => !catalog.Contains(p)))
            {
                violations.Add($"{label} requires '{unknown}', which no module registers in the permission catalog.");
            }

            if (IsAnonymous(route))
            {
                if (!allowsAnonymous)
                {
                    violations.Add($"{label} is on the anonymous list but does not call .AllowAnonymous().");
                }

                continue;
            }

            // Tier 1.
            if (allowsAnonymous || !authorized)
            {
                violations.Add($"{label} is not authorized{(allowsAnonymous ? " (AllowAnonymous)" : "")}. "
                    + "Add .RequirePermission(...), or add it to AnonymousRoutes with the reason it is public.");
                continue;
            }

            // Tier 2.
            if (!namesPermission && !AuthenticatedOnlyRoutes.Contains(route))
            {
                violations.Add($"{label} requires a signed-in caller but no permission. Add "
                    + ".RequirePermission(...), or add it to AuthenticatedOnlyRoutes with the reason any "
                    + "signed-in user may call it.");
            }
        }

        foreach (string listed in AnonymousRoutes.Concat(AuthenticatedOnlyRoutes).Except(NotYetMapped))
        {
            if (!seen.Contains(listed))
            {
                violations.Add($"{listed} is listed in Rule 12 but no such route is mapped. Remove the entry.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        // The runtime half of K7: a route that declares nothing is not anonymous. Checked on the same
        // host, so a FallbackPolicy configured somewhere this host doesn't run would fail here.
        AuthorizationPolicy? fallback = _host.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;
        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);

        // PC-03 liveness. Both tiers pass trivially over an empty data source: a host that failed to map
        // a module, or a lookup that returned a different EndpointDataSource. Counted at Phase 1A: 330 (331 at
        // 1B, with /api/auth/logout)
        // route endpoints (every module, plus /api/me, /health, the hub and the Development API docs).
        // The margin absorbs a few routes being retired; losing a module's worth is a red test.
        Assert.True(endpoints.Length >= 320,
            $"Rule 12 inspected only {endpoints.Length} route endpoints, expected at least 320 (330 counted at "
            + "Phase 1A). The host is not mapping what Program.cs maps, so this rule is checking nothing.");
    }

    private static bool IsAnonymous(string route) =>
        AnonymousRoutes.Contains(route) || AnonymousPrefixes.Any(p => route.StartsWith(p, StringComparison.Ordinal));

    private static string Normalize(string? rawText)
    {
        string route = "/" + (rawText ?? string.Empty).Trim('/');
        return route;
    }

    private static string Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods
            ? string.Join(",", methods)
            : "ANY";

    private sealed class HostFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development maps the API docs too, so the rule sees the largest route set the host has.
            builder.UseEnvironment("Development");
            builder.UseSetting("JwtSettings:Key", "rule12-signing-key-at-least-32-bytes-long-0000000000");
            builder.UseSetting("JwtSettings:Issuer", "rule12");
            builder.UseSetting("JwtSettings:Audience", "rule12");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Crm:PublicLeadIntakeOwnerId", Guid.Empty.ToString());
            builder.UseSetting(BuildingBlocks.BackgroundLoops.EnabledKey, "false");
        }
    }
}
