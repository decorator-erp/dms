using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Documents.Contracts;
using Documents.Infrastructure.Persistence;
using Documents.Infrastructure.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;

namespace Documents.Infrastructure;

/// <summary>
/// G7-07: the pluggable <c>IGateConditionTypeHandler</c> registration and <c>AddModuleIntegrationEvents</c>
/// both land here, alongside the Files feature and gate handlers (<c>AttachmentHandler</c>,
/// <c>DocumentApprovedHandler</c>) they scan for — structural item 2 fully wired. G7-08 adds
/// <c>IDocumentQueries</c> and the endpoint mapping's <c>FormOptions</c> hardening.
/// </summary>
internal sealed class DocumentsModule : IModule
{
    public string Name => "Documents";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddModuleDbContext<DocumentsDbContext>(cfg);

        // Singleton, matching the backend - stateless beyond the resolved root path, computed once from
        // configuration at construction (G7-05).
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IDocumentQueries, DocumentQueries>();

        // Same fallback default LocalFileStorageService's own constructor uses (kept in sync there, not
        // shared via a constant - a small, documented duplication, same trade-off ProjectsEnumMappingShim
        // already accepts elsewhere in this codebase). Caps the multipart form parser's own work at
        // roughly the configured ceiling, rejecting a wildly oversized body before a form value provider
        // spends any time on it - SaveAsync's own chunk-by-chunk enforcement (G7-05) remains the
        // authoritative backstop regardless of what this allows through.
        var maxUploadBytes = long.TryParse(cfg["FileStorage:MaxUploadBytes"], out var configuredMax) && configuredMax > 0
            ? configuredMax
            : 52_428_800;
        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadBytes);

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
