using System.Reflection;
using System.Text;
using Api;
using Api.Authorization;
using BuildingBlocks;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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

// Zero modules exist yet (Identity lands at G1-03, HR at G2-03) — this becomes a real assembly list
// then. The composition root is the only place that ever sees every module.
var moduleAssemblies = Array.Empty<Assembly>();
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

app.Run();

// Exposes the auto-generated entry-point class to WebApplicationFactory<Program> in the (external)
// integration test assembly at G0-09 — top-level statements otherwise generate it `internal`.
#pragma warning disable CA1050
public partial class Program;
#pragma warning restore CA1050
