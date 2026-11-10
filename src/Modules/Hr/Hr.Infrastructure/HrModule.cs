using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Hr.Contracts;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Persistence.Seeders;
using Identity.Contracts;
using Hr.Infrastructure.Services;
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
        services.AddModuleDbContext<HrDbContext>(cfg);

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

        services.AddScoped<IHrDirectory, HrDirectory>();
        services.AddScoped<IDepartmentMembershipService, DepartmentMembershipService>();

        services.AddModuleCqrs<HrDbContext>(Assembly.Load("Hr.Application"));

        // The integration event handler (EmployeeDeactivatedIntegrationEventHandler) lives in THIS
        // assembly, not Hr.Application - unlike command/query handlers, it belongs to Infrastructure
        // because it calls out to another module's contract (Identity.Contracts.IUserAdministration),
        // which Hr.Application has no reason to know about.
        services.AddModuleIntegrationEvents<HrDbContext>(typeof(HrModule).Assembly);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(HrModule).Assembly);

    /// <summary>Every login is an employee or a client: the seeded admin gets its employee profile here,
    /// after Identity has seeded the user (see <see cref="AdminEmployeeSeeder"/>).</summary>
    public async Task SeedAsync(IServiceProvider services, CancellationToken ct = default) =>
        await AdminEmployeeSeeder.SeedAsync(
            services.GetRequiredService<HrDbContext>(), services.GetRequiredService<IUserDirectory>(), ct);
}
