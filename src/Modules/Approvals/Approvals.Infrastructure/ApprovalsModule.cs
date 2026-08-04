using System.Reflection;
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
/// No <c>AddModuleIntegrationEvents</c> call — unlike HR, Approvals has no
/// <c>IIntegrationEventHandler&lt;T&gt;</c> of its own (it only publishes, via
/// <c>db.Outbox().Enqueue(...)</c> directly in its command handlers; nothing in this module subscribes to
/// another module's events). Per <c>docs/adding-a-module.md</c>: call it only "if this module handles any
/// integration events" — Approvals doesn't, yet.
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

        services.AddModuleCqrs<ApprovalsDbContext>(Assembly.Load("Approvals.Application"));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(ApprovalsModule).Assembly);
}
