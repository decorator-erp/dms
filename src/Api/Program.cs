using System.Reflection;
using Api;
using BuildingBlocks;
using BuildingBlocks.Web;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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
var moduleAssemblies = new[]
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
var modules = ModuleDiscovery.DiscoverModules(moduleAssemblies);

foreach (var module in modules)
    module.Register(builder.Services, builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.UseCors(CorsPolicies.Default);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseSerilogRequestLogging();

app.MapHealthChecks("/health");

// A minimal authenticated "who am I" endpoint — generically useful on its own, and gives G0-09's
// smoke test a real protected route to assert 401-unauthenticated against before any module exists.
app.MapGet("/api/me", (ICurrentUser currentUser) => Results.Ok(new { currentUser.UserId }))
    .RequireAuthorization();

foreach (var module in modules)
    module.MapEndpoints(app);

// Idempotent by contract (see IModule.SeedAsync) — safe to run on every startup, not just the first.
using (var seedScope = app.Services.CreateScope())
{
    foreach (var module in modules)
        await module.SeedAsync(seedScope.ServiceProvider);
}

app.Run();

// Exposes the auto-generated entry-point class to WebApplicationFactory<Program> in the (external)
// integration test assembly at G0-09 — top-level statements otherwise generate it `internal`.
#pragma warning disable CA1050
public partial class Program;
#pragma warning restore CA1050
