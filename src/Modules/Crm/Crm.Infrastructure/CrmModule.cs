using BuildingBlocks;
using BuildingBlocks.Web;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Crm.Infrastructure;

/// <summary>
/// `Register` grows across the remaining Phase 4 tasks — `AddModuleCqrs` at G9-09 once
/// `Crm.Application` exists, the `IGateConditionTypeHandler` Scrutor scan and
/// `AddModuleIntegrationEvents` at G9-13 once the gate handlers and the `ApprovalDecided` subscriber
/// exist, `IClientDirectory`/`ICrmAccessGuard`/`ICrmGateReader`/`ICrmBriefReader` implementations as
/// they land — same incremental shape `DocumentsModule`'s own history took across G7-01→G7-08.
/// </summary>
internal sealed class CrmModule : IModule
{
    public string Name => "Crm";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<CrmDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(Name, CrmPermissions.All));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(CrmModule).Assembly);
}
