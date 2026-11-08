using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// P2-A, client portal visibility.
///
/// <para><b>Office</b> (<c>/api/crm/projects/{projectId}/portal</c>): reads need <c>crm.portal.manage</c> or
/// <c>clients.read</c>; writes need <c>crm.portal.manage</c>. Every message is <c>ICrmProjectScoped</c>, so the order is
/// route claim 403 → project scope 404 → state 409.</para>
///
/// <para><b>Client</b> (<c>/api/crm/portal/projects/{projectId}</c>): the handlers check the project is the caller's
/// own (else 404) and that the section is on. Files go only through <c>files/{shareId}</c> — inline for images, as an
/// attachment otherwise — and no client response carries a document id.</para>
/// </summary>
internal sealed class CrmPortalVisibilityEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        MapOffice(app.MapGroup("/api/crm/projects/{projectId:guid}/portal").WithTags("Crm Portal Management"));
        MapClient(app.MapGroup("/api/crm/portal/projects/{projectId:guid}").WithTags("Crm Portal"));
    }

    private static void MapOffice(RouteGroupBuilder office)
    {
        office.MapGet("/settings", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetCrmPortalSettingsQuery, CrmPortalSettingsResponse> handler,
            CancellationToken ct) =>
        {
            Result<CrmPortalSettingsResponse> result = await handler.HandleAsync(new GetCrmPortalSettingsQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequireAnyPermission(CrmPermissions.PortalVisibility.Manage, CrmPermissions.Clients.Read);

        office.MapPut("/settings", async (
            Guid projectId, PortalSettingsRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateCrmPortalSettingsCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateCrmPortalSettingsCommand(
                projectId, request.Progress, request.Tasks, request.Documents, request.Gallery, request.SiteSurveys, request.Updates,
                currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapGet("/shares", async (
            Guid projectId, PortalShareKind? kind, ICurrentUser currentUser,
            IQueryHandler<GetCrmPortalSharesQuery, IReadOnlyList<CrmPortalShareResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmPortalShareResponse>> result = await handler.HandleAsync(
                new GetCrmPortalSharesQuery(projectId, currentUser.UserId!, kind), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequireAnyPermission(CrmPermissions.PortalVisibility.Manage, CrmPermissions.Clients.Read);

        office.MapPost("/shares", async (
            Guid projectId, CreateShareRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateCrmPortalShareCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateCrmPortalShareCommand(
                projectId, request.ItemKind, request.ItemId, request.ClientTitle, request.ClientNote, request.FollowLatest ?? true,
                request.SortOrder, request.NotifyClient, currentUser.UserId!), ct);
            return result.Match(
                id => Results.Created($"/api/crm/projects/{projectId}/portal/shares/{id}", new { ShareId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        // Before "/shares/{shareId:guid}" in reading order only; the guid constraint already keeps "order" apart.
        office.MapPut("/shares/order", async (
            Guid projectId, ShareOrderRequest request, ICurrentUser currentUser,
            ICommandHandler<ReorderCrmPortalSharesCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new ReorderCrmPortalSharesCommand(projectId, request.ShareIds ?? [], currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapPut("/shares/{shareId:guid}", async (
            Guid projectId, Guid shareId, UpdateShareRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateCrmPortalShareCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateCrmPortalShareCommand(
                projectId, shareId, request.ClientTitle, request.ClientNote, request.FollowLatest ?? true, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapDelete("/shares/{shareId:guid}", async (
            Guid projectId, Guid shareId, ICurrentUser currentUser, ICommandHandler<DeleteCrmPortalShareCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteCrmPortalShareCommand(projectId, shareId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapGet("/updates", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetCrmPortalUpdatesQuery, IReadOnlyList<CrmPortalUpdateResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmPortalUpdateResponse>> result = await handler.HandleAsync(
                new GetCrmPortalUpdatesQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequireAnyPermission(CrmPermissions.PortalVisibility.Manage, CrmPermissions.Clients.Read);

        office.MapPost("/updates", async (
            Guid projectId, UpdateRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateCrmPortalUpdateCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateCrmPortalUpdateCommand(
                projectId, request.Title ?? string.Empty, request.Body, request.ShareIds ?? [], currentUser.UserId!), ct);
            return result.Match(
                id => Results.Created($"/api/crm/projects/{projectId}/portal/updates/{id}", new { UpdateId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapPut("/updates/{updateId:guid}", async (
            Guid projectId, Guid updateId, UpdateRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateCrmPortalUpdateCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateCrmPortalUpdateCommand(
                projectId, updateId, request.Title ?? string.Empty, request.Body, request.ShareIds ?? [], currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapDelete("/updates/{updateId:guid}", async (
            Guid projectId, Guid updateId, ICurrentUser currentUser, ICommandHandler<DeleteCrmPortalUpdateCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteCrmPortalUpdateCommand(projectId, updateId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapPost("/updates/{updateId:guid}/publish", async (
            Guid projectId, Guid updateId, PublishRequest? request, ICurrentUser currentUser,
            ICommandHandler<PublishCrmPortalUpdateCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new PublishCrmPortalUpdateCommand(
                projectId, updateId, request?.NotifyClient ?? false, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.Manage);

        office.MapGet("/preview", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetCrmPortalPreviewQuery, CrmPortalPreviewResponse> handler,
            CancellationToken ct) =>
        {
            Result<CrmPortalPreviewResponse> result = await handler.HandleAsync(new GetCrmPortalPreviewQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequireAnyPermission(CrmPermissions.PortalVisibility.Manage, CrmPermissions.Clients.Read);
    }

    private static void MapClient(RouteGroupBuilder client)
    {
        client.MapGet("/overview", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetMyPortalOverviewQuery, PortalOverviewResponse> handler,
            CancellationToken ct) =>
        {
            Result<PortalOverviewResponse> result = await handler.HandleAsync(new GetMyPortalOverviewQuery(currentUser.UserId!, projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.ProgressViewOwn);

        client.MapGet("/tasks", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetMyPortalTasksQuery, IReadOnlyList<PortalTaskResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<PortalTaskResponse>> result = await handler.HandleAsync(new GetMyPortalTasksQuery(currentUser.UserId!, projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.ProgressViewOwn);

        client.MapGet("/updates", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetMyPortalUpdatesQuery, IReadOnlyList<PortalUpdateResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<PortalUpdateResponse>> result = await handler.HandleAsync(new GetMyPortalUpdatesQuery(currentUser.UserId!, projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.ProgressViewOwn);

        client.MapGet("/files", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetMyPortalFilesQuery, IReadOnlyList<PortalFileResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<PortalFileResponse>> result = await handler.HandleAsync(new GetMyPortalFilesQuery(currentUser.UserId!, projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.FilesViewOwn);

        client.MapGet("/gallery", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetMyPortalGalleryQuery, IReadOnlyList<PortalFileResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<PortalFileResponse>> result = await handler.HandleAsync(new GetMyPortalGalleryQuery(currentUser.UserId!, projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.PortalVisibility.FilesViewOwn);

        // Streams the file without buffering it. Images inline (a lightbox can show them); everything else as an
        // attachment. The response names the file, never its document id.
        client.MapGet("/files/{shareId:guid}", async (
            Guid projectId, Guid shareId, ICurrentUser currentUser, HttpContext http,
            IQueryHandler<GetMyPortalFileContentQuery, PortalFileContentResponse> handler, CancellationToken ct) =>
        {
            Result<PortalFileContentResponse> result = await handler.HandleAsync(
                new GetMyPortalFileContentQuery(currentUser.UserId!, projectId, shareId), ct);
            if (result.IsFailure)
            {
                return result.ToProblem();
            }

            PortalFileContentResponse file = result.Value!;
            if (!file.Inline)
            {
                return Results.Stream(file.Content, file.ContentType, file.FileName);
            }

            var disposition = new ContentDispositionHeaderValue("inline");
            disposition.SetHttpFileName(file.FileName);
            http.Response.Headers.ContentDisposition = disposition.ToString();
            return Results.Stream(file.Content, file.ContentType);
        }).RequirePermission(CrmPermissions.PortalVisibility.FilesViewOwn);
    }

    private sealed record PortalSettingsRequest(bool Progress, bool Tasks, bool Documents, bool Gallery, bool SiteSurveys, bool Updates);

    private sealed record CreateShareRequest(
        PortalShareKind ItemKind, Guid ItemId, string? ClientTitle = null, string? ClientNote = null, bool? FollowLatest = null,
        int? SortOrder = null, bool NotifyClient = false);

    private sealed record UpdateShareRequest(string? ClientTitle, string? ClientNote, bool? FollowLatest = null);

    private sealed record ShareOrderRequest(IReadOnlyList<Guid>? ShareIds);

    private sealed record UpdateRequest(string? Title, string? Body, IReadOnlyList<Guid>? ShareIds = null);

    private sealed record PublishRequest(bool NotifyClient = false);
}
