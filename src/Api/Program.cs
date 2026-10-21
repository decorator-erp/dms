using System.Reflection;
using Api;
using BuildingBlocks;
using BuildingBlocks.Web;
using Scalar.AspNetCore;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// writeToProviders: true is not decoration - N1-08 found it missing the hard way.
//
// By default UseSerilog REPLACES the whole ILoggerProvider set, so any provider registered through
// ConfigureLogging is silently dropped. ApiFixture registers a SqlCapturingLoggerProvider to record
// every "Executed DbCommand" for the schema-isolation tests, and it was never receiving a single
// event - which meant CrmSchemaIsolationTests, ProjectsSchemaIsolationTests,
// DocumentsSchemaIsolationTests and CrossModuleSmokeTests' identity assertion were all passing
// VACUOUSLY: "no captured SQL touches schema X" is trivially true when nothing is captured at all.
// Proved by asserting CapturedSql was non-empty after a request that certainly queries, and watching
// that fail.
//
// This flag makes Serilog forward events to other registered providers as well as its own sinks. In
// production nothing else is registered, so it costs nothing there; in tests it is the difference
// between four real assertions and four decorative ones.
builder.Host.UseSerilog(
    (context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration),
    writeToProviders: true);

builder.Services.AddApi(builder.Configuration);

// Loaded by assembly NAME, not by referencing any type from it — every IModule implementation is
// internal (only *.Contracts is public), so Api can never name one directly even though it references
// the project. HR joins this list at G2-03. The composition root is the only place that ever sees
// every module.
Assembly[] moduleAssemblies = new[]
{
    Assembly.Load("Identity.Infrastructure"),
    Assembly.Load("Hr.Infrastructure"),
    Assembly.Load("Approvals.Infrastructure"),
    Assembly.Load("Crm.Infrastructure"),
    Assembly.Load("Projects.Infrastructure"),
    Assembly.Load("Documents.Infrastructure"),
    Assembly.Load("Sites.Infrastructure"),
    // F1-09. Referenced by Api since F1-04 (so ApiFixture could migrate the schema) but not registered
    // until now, because a module registers when it has something to serve — 54 routes across ten
    // endpoint classes, of which sixteen live under Projects' URL space and are served from here.
    Assembly.Load("Finance.Infrastructure"),
    // N1-04, three tasks before this module has an endpoint to serve. Loaded now because MapEndpoints
    // is what mounts NotificationsHub, and because N1-05's relocated jobs resolve INotificationService
    // from this container. CrmSmokeTests.Boot_RegistersExactlyEightModules moves in the same commit.
    Assembly.Load("Notifications.Infrastructure"),
};
IReadOnlyList<IModule> modules = ModuleDiscovery.DiscoverModules(moduleAssemblies);

foreach (IModule module in modules)
{
    module.Register(builder.Services, builder.Configuration);
}

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development-only API docs. Anonymous explicitly, because the FallbackPolicy (K7) would otherwise
    // put the docs UI behind a login it has no way to perform.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// PF-D2: first, so everything after it (the IP-keyed rate limits included) sees the real client IP. It
// only trusts the proxies named in ForwardedHeaders:KnownProxies / :KnownNetworks (Api DI.cs).
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseCors(CorsPolicies.Default);
app.UseHttpsRedirection();
// PF-D2: authentication BEFORE rate limiting, so AuthSession can key /change-password and /logout by the
// signed-in user rather than by IP. Phase 1B had it the other way round, so a throttled request never
// reached N2's session check (a database read). What that cost now is bounded: only a validly signed
// token triggers the read, IUserSessionState caches it, and every non-auth route already ran the check
// with no rate limit at all.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseSerilogRequestLogging();

// Anonymous on purpose: load balancers and container orchestrators probe it without credentials. Explicit
// because the FallbackPolicy (K7) would otherwise require authentication here too.
app.MapHealthChecks("/health").AllowAnonymous();

// A minimal authenticated "who am I" endpoint — generically useful on its own, and gives G0-09's
// smoke test a real protected route to assert 401-unauthenticated against before any module exists.
app.MapGet("/api/me", (ICurrentUser currentUser) => Results.Ok(new { currentUser.UserId }))
    .RequireAuthorization();

foreach (IModule module in modules)
{
    module.MapEndpoints(app);
}

// Idempotent by contract (see IModule.SeedAsync) — safe to run on every startup, not just the first.
using (IServiceScope seedScope = app.Services.CreateScope())
{
    foreach (IModule module in modules)
    {
        await module.SeedAsync(seedScope.ServiceProvider);
    }
}

app.Run();

// Exposes the auto-generated entry-point class to WebApplicationFactory<Program> in the (external)
// integration test assembly at G0-09 — top-level statements otherwise generate it `internal`.
#pragma warning disable CA1050
public partial class Program;
#pragma warning restore CA1050
