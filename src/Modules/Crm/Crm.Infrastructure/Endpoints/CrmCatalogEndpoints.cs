using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// Catalog category/item/attribute CRUD, governed by <see cref="CrmPermissions.Catalog"/>. Narrower
/// than the backend's own V2 surface (no update/delete/reorder/clone/active-toggle for categories or
/// items, no space-type unmap) — G9-11's own vertical-slice scope, ported here as-is rather than
/// invented at this endpoints task; see this task's PROGRESS.md entry for the full breadth-deferral
/// list.
/// </summary>
internal sealed class CrmCatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder catalog = app.MapGroup("/api/crm/catalog").WithTags("Crm Catalog");

        catalog.MapPost("/categories", async (
            CreateCrmCatalogCategoryCommand command, ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Created($"/api/crm/catalog/categories/{id}", new { CategoryId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Manage);

        catalog.MapPut("/categories/{categoryId:guid}/parent", async (
            Guid categoryId, MoveCategoryRequest request, ICommandHandler<MoveCrmCatalogCategoryCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new MoveCrmCatalogCategoryCommand(categoryId, request.NewParentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Manage);

        catalog.MapPost("/categories/{categoryId:guid}/space-types/{spaceTypeId:guid}", async (
            Guid categoryId, Guid spaceTypeId, ICommandHandler<MapCrmCatalogCategoryToSpaceTypeCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new MapCrmCatalogCategoryToSpaceTypeCommand(categoryId, spaceTypeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Manage);

        catalog.MapPost("/items", async (
            CreateCrmCatalogItemCommand command, ICommandHandler<CreateCrmCatalogItemCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Created($"/api/crm/catalog/items/{id}", new { ItemId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Manage);

        catalog.MapPut("/items/{itemId:guid}/attribute-values", async (
            Guid itemId, SetItemAttributeValuesRequest request,
            ICommandHandler<SetCrmCatalogItemAttributeValuesCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SetCrmCatalogItemAttributeValuesCommand(itemId, request.Values), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Manage);

        catalog.MapPost("/attributes", async (
            CreateCrmCatalogAttributeCommand command, ICommandHandler<CreateCrmCatalogAttributeCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Created($"/api/crm/catalog/attributes/{id}", new { AttributeId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Manage);

        catalog.MapPost("/attributes/{attributeId:guid}/options", async (
            Guid attributeId, CreateAttributeOptionRequest request,
            ICommandHandler<CreateCrmCatalogAttributeOptionCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateCrmCatalogAttributeOptionCommand(
                attributeId, request.Code, request.NameEn, request.NameAr, request.SortOrder, request.ImageUri), ct);
            return result.Match(
                id => Results.Created($"/api/crm/catalog/attributes/{attributeId}/options/{id}", new { OptionId = id }),
                r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Manage);

        catalog.MapGet("/tree", async (
            IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmCatalogCategoryResponse>> result = await handler.HandleAsync(new GetCrmCatalogTreeQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Read);

        catalog.MapGet("/categories/{categoryId:guid}/items", async (
            Guid categoryId, IQueryHandler<GetCrmCatalogItemsByCategoryQuery, IReadOnlyList<CrmCatalogItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmCatalogItemResponse>> result = await handler.HandleAsync(new GetCrmCatalogItemsByCategoryQuery(categoryId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Read);

        catalog.MapGet("/categories/{categoryId:guid}/attributes", async (
            Guid categoryId, IQueryHandler<GetCrmCatalogAttributesByCategoryQuery, IReadOnlyList<CrmCatalogAttributeResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmCatalogAttributeResponse>> result = await handler.HandleAsync(new GetCrmCatalogAttributesByCategoryQuery(categoryId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Catalog.Read);
    }

    private sealed record MoveCategoryRequest(Guid? NewParentId);
    private sealed record SetItemAttributeValuesRequest(IReadOnlyList<CatalogAttributeValueData> Values);
    private sealed record CreateAttributeOptionRequest(string Code, string NameEn, string NameAr, int SortOrder = 0, string? ImageUri = null);
}
