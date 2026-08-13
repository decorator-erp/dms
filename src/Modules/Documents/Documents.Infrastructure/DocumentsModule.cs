using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Web;
using Documents.Contracts;
using Documents.Infrastructure.Persistence;
using Documents.Infrastructure.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Documents.Infrastructure;

/// <summary>
/// G7-06: <c>AddModuleCqrs</c> now wired, since <c>Documents.Application</c>'s first real features
/// (Folders, FileTemplates) exist to scan. The pluggable <c>IGateConditionTypeHandler</c> registration
/// and <c>AddModuleIntegrationEvents</c> both still land at G7-07, alongside the Files feature and gate
/// handlers they scan for.
/// </summary>
internal sealed class DocumentsModule : IModule
{
    public string Name => "Documents";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<DocumentsDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        // Singleton, matching the backend - stateless beyond the resolved root path, computed once from
        // configuration at construction (G7-05).
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();

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

        services.AddModuleCqrs<DocumentsDbContext>(Assembly.Load("Documents.Application"));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(DocumentsModule).Assembly);
}
