using System.Text;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Api;

public static class DI
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

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

        // SharedDbContext + AddOutbox() were never wired into the composition root before this - every
        // module's outbox writes (UserRegisteredIntegrationEvent, EmployeeHiredIntegrationEvent, etc.) landed
        // in shared.outbox_messages successfully (each module maps that table on its own DbContext), but
        // nothing was ever registered to poll and publish them. Found while wiring G2-04's deactivate -> outbox
        // -> Identity flow, which is the first feature that actually depends on a message getting delivered.
        services.AddDbContext<SharedDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));
        services.AddOutbox();

        return services;
    }
}
