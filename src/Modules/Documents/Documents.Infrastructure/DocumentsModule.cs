using BuildingBlocks;
using BuildingBlocks.Web;
using Documents.Contracts;
using Documents.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Documents.Infrastructure;

/// <summary>
/// G7-03: DbContext + permission registration only — <c>AddModuleCqrs</c> waits for
/// <c>Documents.Application</c> to exist (G7-06/07), same "Register without AddModuleCqrs yet" sequence
/// <c>docs/adding-a-module.md</c>'s wiring checklist describes for every module's own bootstrap. The
/// pluggable <c>IGateConditionTypeHandler</c> registration and <c>AddModuleIntegrationEvents</c> both
/// land at G7-07, alongside the Application code they scan.
/// </summary>
internal sealed class DocumentsModule : IModule
{
    public string Name => "Documents";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<DocumentsDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            Name,
            [
                DocumentsPermissions.Read,
                DocumentsPermissions.Upload,
                DocumentsPermissions.Update,
                DocumentsPermissions.Delete,
                DocumentsPermissions.FileTemplatesRead,
                DocumentsPermissions.FileTemplatesManage,
            ]));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(DocumentsModule).Assembly);
}
