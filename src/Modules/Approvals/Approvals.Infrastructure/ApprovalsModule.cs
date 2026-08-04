using Approvals.Contracts;
using Approvals.Infrastructure.Persistence;
using Approvals.Infrastructure.Services;
using BuildingBlocks;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Approvals.Infrastructure;

/// <summary>
/// <c>AddModuleCqrs</c>/<c>AddModuleIntegrationEvents</c> and endpoint mapping deliberately are NOT wired
/// here yet — <c>Approvals.Application</c> doesn't exist until G4-04, and this module gets exercised by
/// G4-03's own migration/rule-9 tests via a throwaway <c>Register</c> call
/// (<c>Rule9CrossSchemaForeignKeyTests</c>'s pattern): calling <c>Assembly.Load("Approvals.Application")</c>
/// before that DLL exists would throw. Same split HR used (G2-03 persistence-only, G2-06 added
/// endpoints once G2-04/05's Application handlers existed) — completed at G4-04.
/// </summary>
internal sealed class ApprovalsModule : IModule
{
    public string Name => "Approvals";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<ApprovalsDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            Name,
            [
                ApprovalsPermissions.Read,
                ApprovalsPermissions.Decide,
                ApprovalsPermissions.DecideAny,
            ]));

        services.AddScoped<IApprovalRequester, ApprovalRequester>();

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(ApprovalsModule).Assembly);
}
