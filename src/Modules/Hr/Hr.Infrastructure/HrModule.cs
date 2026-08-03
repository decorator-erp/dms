using BuildingBlocks;
using BuildingBlocks.Web;
using Hr.Contracts;
using Hr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hr.Infrastructure;

internal sealed class HrModule : IModule
{
    public string Name => "Hr";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<HrDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            Name,
            [
                HrPermissions.EmployeesRead,
                HrPermissions.EmployeesViewOwn,
                HrPermissions.EmployeesUpdate,
                HrPermissions.EmployeesSetActive,
                HrPermissions.EmployeesCreate,
                HrPermissions.EmployeesDelete,
                HrPermissions.DepartmentsRead,
                HrPermissions.DepartmentsViewUsers,
                HrPermissions.DepartmentsUpdate,
                HrPermissions.DepartmentsSetActive,
                HrPermissions.DepartmentsCreate,
                HrPermissions.DepartmentsDelete,
                HrPermissions.DepartmentsAssignDepartmentHead,
                HrPermissions.DepartmentsUnassignDepartmentHead,
                HrPermissions.DepartmentsAssignToUser,
                HrPermissions.DepartmentsRemoveFromUser,
                HrPermissions.DepartmentsMoveUser,
            ]));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(HrModule).Assembly);
}
