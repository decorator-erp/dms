using System.Reflection;
using Approvals.Contracts;
using Approvals.Infrastructure.Persistence;
using Approvals.Infrastructure.Services;
using BuildingBlocks;
using BuildingBlocks.Jobs;
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
        services.AddModuleDbContext<ApprovalsDbContext>(cfg);

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            Name,
            [
                ApprovalsPermissions.Read,
                ApprovalsPermissions.Decide,
                ApprovalsPermissions.DecideAny,
            ]));

        services.AddScoped<IApprovalRequester, ApprovalRequester>();

        var applicationAssembly = Assembly.Load("Approvals.Application");
        services.AddModuleCqrs<ApprovalsDbContext>(applicationAssembly);

        // N1-05: the two approval-SLA jobs, run by BuildingBlocks' single PeriodicJobWorker. Same
        // define-in-one-place / implement-in-many shape as IGateConditionTypeHandler, and the same
        // Scrutor technique for the same reason - the jobs are `internal` in Approvals.Application,
        // which grants this assembly no InternalsVisibleTo, so only the PUBLIC interface can be named.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IPeriodicJob>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(ApprovalsModule).Assembly);
}
