using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Definition;

namespace Sites.Infrastructure.Endpoints;

/// <summary>
/// The form engine — global master data with no project and no site, and therefore deliberately outside
/// the row-level scope decorator entirely.
///
/// <para>🔴 <b>Reads are gated on <see cref="SitesPermissions.Read"/>, writes on
/// <see cref="SitesPermissions.Definitions.Manage"/>.</b> That split is the reason
/// <c>sites.definitions.manage</c> exists as a seventh permission and the reason S1-01's original
/// six-string count was wrong: gating the reads on the write permission would mean a surveyor could not
/// render the capture form, because the form cannot draw without the schema that shapes it.</para>
/// </summary>
internal sealed class SiteDefinitionEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder modules = app.MapGroup("/api/site-modules").WithTags("Site Definitions");

        modules.MapGet("/", async (
            // Nullable, not bare bool: a non-nullable query parameter is REQUIRED in minimal APIs, so
            // GET /api/site-modules with no query string would 400 rather than list the active modules.
            bool? includeInactive,
            IQueryHandler<GetSiteModulesQuery, IReadOnlyList<SiteModuleResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<SiteModuleResponse>> result = await handler.HandleAsync(new GetSiteModulesQuery(includeInactive ?? false), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        // The surveyor's form. Sites.Read deliberately -- see the type summary.
        modules.MapGet("/{code}/schema", async (
            string code,
            IQueryHandler<GetSiteModuleSchemaQuery, SiteModuleSchemaResponse> handler,
            CancellationToken ct) =>
        {
            Result<SiteModuleSchemaResponse> result = await handler.HandleAsync(new GetSiteModuleSchemaQuery(code), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        modules.MapPost("/", async (
            CreateSiteModuleRequest request,
            ICommandHandler<CreateSiteModuleCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateSiteModuleCommand(request), ct);
            return result.Match(
                id => Results.Created($"/api/site-modules/{id}", new { ModuleId = id }),
                r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        modules.MapPut("/{moduleId:guid}", async (
            Guid moduleId, UpdateSiteModuleRequest request,
            ICommandHandler<UpdateSiteModuleCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateSiteModuleCommand(moduleId, request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        modules.MapDelete("/{moduleId:guid}", async (
            Guid moduleId, ICommandHandler<DeleteSiteModuleCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteSiteModuleCommand(moduleId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        modules.MapPut("/{moduleId:guid}/active", async (
            Guid moduleId, bool isActive,
            ICommandHandler<SetSiteModuleActiveCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SetSiteModuleActiveCommand(moduleId, isActive), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        // ── Links: which items appear on which module's form ───────────────────

        modules.MapPost("/{moduleId:guid}/items", async (
            Guid moduleId, AttachSiteItemRequest request,
            ICommandHandler<AttachSiteItemCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new AttachSiteItemCommand(moduleId, request), ct);
            return result.Match(
                id => Results.Created($"/api/site-modules/{moduleId}/items/{request.ItemId}", new { LinkId = id }),
                r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        modules.MapPut("/{moduleId:guid}/items/reorder", async (
            Guid moduleId, ReorderSiteLinksRequest request,
            ICommandHandler<ReorderSiteLinksCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ReorderSiteLinksCommand(moduleId, request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        modules.MapPut("/{moduleId:guid}/items/{itemId:guid}", async (
            Guid moduleId, Guid itemId, UpdateSiteLinkRequest request,
            ICommandHandler<UpdateSiteLinkCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateSiteLinkCommand(moduleId, itemId, request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        modules.MapDelete("/{moduleId:guid}/items/{itemId:guid}", async (
            Guid moduleId, Guid itemId,
            ICommandHandler<DetachSiteItemCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DetachSiteItemCommand(moduleId, itemId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        // ── Items ──────────────────────────────────────────────────────────────

        RouteGroupBuilder items = app.MapGroup("/api/site-items").WithTags("Site Definitions");

        items.MapGet("/", async (
            bool? includeInactive,
            IQueryHandler<GetSiteItemsQuery, IReadOnlyList<SiteItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<SiteItemResponse>> result = await handler.HandleAsync(new GetSiteItemsQuery(includeInactive ?? false), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        items.MapPost("/", async (
            CreateSiteItemRequest request,
            ICommandHandler<CreateSiteItemCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateSiteItemCommand(request), ct);
            return result.Match(
                id => Results.Created($"/api/site-items/{id}", new { ItemId = id }), r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        // Options live under /api/site-items/options/{optionId} while ADDING one lives under
        // /api/site-items/{itemId}/options -- an asymmetry inherited from the backend. The literal
        // "options" segment cannot collide with the {itemId:guid} route, but it is easy to get backwards.
        items.MapPut("/options/{optionId:guid}", async (
            Guid optionId, UpdateSiteItemOptionRequest request,
            ICommandHandler<UpdateSiteItemOptionCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateSiteItemOptionCommand(optionId, request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        items.MapPut("/options/{optionId:guid}/active", async (
            Guid optionId, bool isActive,
            ICommandHandler<SetSiteItemOptionActiveCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new SetSiteItemOptionActiveCommand(optionId, isActive), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        items.MapGet("/{itemId:guid}/usage", async (
            Guid itemId,
            IQueryHandler<GetSiteItemUsageQuery, SiteItemUsageResponse> handler, CancellationToken ct) =>
        {
            Result<SiteItemUsageResponse> result = await handler.HandleAsync(new GetSiteItemUsageQuery(itemId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        items.MapPut("/{itemId:guid}", async (
            Guid itemId, UpdateSiteItemRequest request,
            ICommandHandler<UpdateSiteItemCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateSiteItemCommand(itemId, request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        items.MapPut("/{itemId:guid}/shape", async (
            Guid itemId, ChangeSiteItemShapeRequest request,
            ICommandHandler<ChangeSiteItemShapeCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ChangeSiteItemShapeCommand(itemId, request), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        items.MapDelete("/{itemId:guid}", async (
            Guid itemId, ICommandHandler<DeleteSiteItemCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteSiteItemCommand(itemId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        items.MapPut("/{itemId:guid}/active", async (
            Guid itemId, bool isActive,
            ICommandHandler<SetSiteItemActiveCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SetSiteItemActiveCommand(itemId, isActive), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);

        items.MapPost("/{itemId:guid}/options", async (
            Guid itemId, SiteItemOptionRequest request,
            ICommandHandler<AddSiteItemOptionCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new AddSiteItemOptionCommand(itemId, request), ct);
            return result.Match(
                id => Results.Created($"/api/site-items/options/{id}", new { OptionId = id }),
                r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Definitions.Manage);
    }
}
