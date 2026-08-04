using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using Projects.Infrastructure.Services;

namespace Projects.Infrastructure;

/// <summary>
/// <c>IProjectDirectory</c> (G5-09) and endpoint mapping still land incrementally as each piece is
/// built. <c>ProjectScopeDecorator</c> wiring (G5-07) happens here too, once the first
/// <c>IProjectScoped</c> commands exist to decorate.
/// </summary>
internal sealed class ProjectsModule : IModule
{
    public string Name => "Projects";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<ProjectsDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        services.AddScoped<IProjectAccessGuard, ProjectAccessGuard>();

        services.AddModuleCqrs<ProjectsDbContext>(Assembly.Load("Projects.Application"));

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
            ]));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(ProjectsModule).Assembly);
}
