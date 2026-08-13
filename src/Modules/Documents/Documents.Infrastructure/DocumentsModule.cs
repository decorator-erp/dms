using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Documents.Contracts;
using Documents.Infrastructure.Persistence;
using Documents.Infrastructure.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;

namespace Documents.Infrastructure;

/// <summary>
/// G7-07: the pluggable <c>IGateConditionTypeHandler</c> registration and <c>AddModuleIntegrationEvents</c>
/// both land here, alongside the Files feature and gate handlers (<c>AttachmentHandler</c>,
/// <c>DocumentApprovedHandler</c>) they scan for — structural item 2 fully wired.
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

        var applicationAssembly = Assembly.Load("Documents.Application");
        services.AddModuleCqrs<DocumentsDbContext>(applicationAssembly);

        // AttachmentHandler/DocumentApprovedHandler live in Documents.Application as `internal` classes
        // (Documents.Infrastructure has no InternalsVisibleTo grant from Documents.Application - only
        // the two test assemblies do), so they can't be named directly here. Scrutor's reflection-based
        // Scan needs only the PUBLIC interface type - same technique ProjectsModule already uses for its
        // own four IGateConditionTypeHandler implementations.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IGateConditionTypeHandler>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // ApprovalDecidedIntegrationEventHandler lives in THIS assembly (Infrastructure), not
        // Application - it calls out to Approvals.Contracts.IIntegrationEventHandler<...>, same
        // "Infrastructure owns handlers that call another module's contract" reasoning
        // ProjectsModule's own ApprovalDecidedIntegrationEventHandler registration documents.
        services.AddModuleIntegrationEvents<DocumentsDbContext>(typeof(DocumentsModule).Assembly);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(DocumentsModule).Assembly);
}
