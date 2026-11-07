using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Jobs;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Infrastructure.Decorators;
using Projects.Infrastructure.Persistence;
using Projects.Infrastructure.Services;

namespace Projects.Infrastructure;

internal sealed class ProjectsModule : IModule
{
    public string Name => "Projects";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddModuleDbContext<ProjectsDbContext>(cfg);

        services.AddScoped<IProjectAccessGuard, ProjectAccessGuard>();
        services.AddScoped<IProjectAccessLevelReader, ProjectAccessLevelReader>();
        services.AddScoped<IProjectDirectory, ProjectDirectory>();

        var applicationAssembly = Assembly.Load("Projects.Application");
        services.AddModuleCqrs<ProjectsDbContext>(applicationAssembly);

        // Outermost, called after AddModuleCqrs so it wraps Validation/Logging/DomainEventDispatch -
        // see ProjectScopeRegistration's own remarks. Scans Projects.CONTRACTS, not Projects.Application:
        // every IProjectScoped command/query record is declared in Projects.Contracts (Application only
        // holds their handlers) - scanning applicationAssembly here found zero matches and silently
        // skipped every Decorate call, caught by ProjectFlowTests' scope-decorator test.
        services.AddProjectScopeDecorator(typeof(IProjectScoped).Assembly);

        // The four IGateConditionTypeHandler implementations (ManualCheck/StageReference/TaskReference/
        // Approval, G5-08) live in Projects.Application as `internal` classes - Projects.Infrastructure
        // has no InternalsVisibleTo grant from Projects.Application (only Projects.Application.Tests
        // does), so they can't be named directly here. Scrutor's reflection-based Scan needs only the
        // PUBLIC interface type, same technique ModuleCqrsRegistration/ModuleIntegrationEventRegistration
        // already use for handler discovery.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IGateConditionTypeHandler>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // N1-05: the three deadline jobs, run by BuildingBlocks' single PeriodicJobWorker. IPeriodicJob
        // is the THIRD instance of the define-in-one-place / implement-in-many pattern (after
        // IGateConditionTypeHandler above and the CQRS/integration-event handler scans), and it uses the
        // same Scrutor technique for the same reason - the jobs are `internal` in Projects.Application.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IPeriodicJob>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // ApprovalDecidedIntegrationEventHandler lives in THIS assembly (Infrastructure), not
        // Application - it calls out to Hr.Contracts.IHrDirectory to resolve the deciding employee,
        // same "Infrastructure owns handlers that call another module's contract" reasoning HrModule's
        // own EmployeeDeactivatedIntegrationEventHandler documents.
        services.AddModuleIntegrationEvents<ProjectsDbContext>(typeof(ProjectsModule).Assembly);

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            Name,
            [
                ProjectsPermissions.Projects.Read,
                ProjectsPermissions.Projects.ReadAll,
                ProjectsPermissions.Projects.Create,
                ProjectsPermissions.Projects.Update,
                ProjectsPermissions.Projects.SetActive,
                ProjectsPermissions.Projects.ChangeStatus,
                ProjectsPermissions.Projects.Delete,
                ProjectsPermissions.Projects.ManageMembers,
                ProjectsPermissions.Stages.Read,
                ProjectsPermissions.Stages.Manage,
                ProjectsPermissions.Stages.Complete,
                ProjectsPermissions.Stages.Approve,
                ProjectsPermissions.Stages.Reject,
                ProjectsPermissions.Stages.ManageGates,
                ProjectsPermissions.StageTeams.Manage,
                ProjectsPermissions.StageTasks.Read,
                ProjectsPermissions.StageTasks.Manage,
                ProjectsPermissions.StageTasks.Assign,
                ProjectsPermissions.StageTasks.Complete,
                ProjectsPermissions.StageTasks.RequestApproval,
                ProjectsPermissions.StageTasks.DecideApproval,
                ProjectsPermissions.StageTasks.ManageGates,
                .. ProjectsPermissions.Scope.All,
            ]));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(ProjectsModule).Assembly);
}
