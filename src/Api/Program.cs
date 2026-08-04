using System.Reflection;
using System.Text;
using Api;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

var jwtKey = builder.Configuration["JwtSettings:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "JwtSettings:Key is not configured. Set it with "
        + "`dotnet user-secrets set \"JwtSettings:Key\" \"<a long random value>\" --project src/Api` "
        + "for local development, or via configured environment variables elsewhere. There is no "
        + "default — a committed signing key is a credential in the repository.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddCors(corsOptions =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    corsOptions.AddPolicy(CorsPolicies.Default, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddHealthChecks();

// SharedDbContext + AddOutbox() were never wired into the composition root before this - every
// module's outbox writes (UserRegisteredIntegrationEvent, EmployeeHiredIntegrationEvent, etc.) landed
// in shared.outbox_messages successfully (each module maps that table on its own DbContext), but
// nothing was ever registered to poll and publish them. Found while wiring G2-04's deactivate -> outbox
// -> Identity flow, which is the first feature that actually depends on a message getting delivered.
builder.Services.AddDbContext<SharedDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddOutbox();

// Loaded by assembly NAME, not by referencing any type from it — every IModule implementation is
// internal (only *.Contracts is public), so Api can never name one directly even though it references
// the project. HR joins this list at G2-03. The composition root is the only place that ever sees
// every module.
var moduleAssemblies = new[]
{
    Assembly.Load("Identity.Infrastructure"),
    Assembly.Load("Hr.Infrastructure"),
    Assembly.Load("Approvals.Infrastructure"),
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
