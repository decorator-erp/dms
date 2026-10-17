using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Documents.Contracts;
using Documents.Contracts.Folders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Documents.Infrastructure.Endpoints;

/// <summary>Folder tree CRUD (G7-06). Create → <see cref="DocumentsPermissions.Upload"/>,
/// rename/move → <see cref="DocumentsPermissions.Update"/>, per the backend's own permission mapping
/// (no separate folder permission group exists).</summary>
internal sealed class DocumentFolderEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder folders = app.MapGroup("/api/document-folders").WithTags("DocumentFolders");

        folders.MapPost("/", async (
            CreateDocumentFolderCommand command, ICommandHandler<CreateDocumentFolderCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Ok(new { FolderId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload);

        folders.MapGet("/{folderId:guid}/children", async (
            Guid folderId, IQueryHandler<GetDocumentFolderChildrenQuery, IReadOnlyList<DocumentFolderResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<DocumentFolderResponse>> result = await handler.HandleAsync(new GetDocumentFolderChildrenQuery(folderId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);

        folders.MapPut("/{folderId:guid}/rename", async (
            Guid folderId, RenameFolderRequest request, ICommandHandler<RenameDocumentFolderCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RenameDocumentFolderCommand(folderId, request.Name), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Update);

        folders.MapPut("/{folderId:guid}/move", async (
            Guid folderId, MoveFolderRequest request, ICommandHandler<MoveDocumentFolderCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new MoveDocumentFolderCommand(folderId, request.NewParentFolderId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Update);

        folders.MapDelete("/{folderId:guid}", async (
            Guid folderId, ICommandHandler<DeleteDocumentFolderCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteDocumentFolderCommand(folderId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Delete);

        RouteGroupBuilder projects = app.MapGroup("/api/projects/{projectId:guid}").WithTags("DocumentFolders");

        projects.MapGet("/document-folders", async (
            Guid projectId, IQueryHandler<GetDocumentFoldersByProjectQuery, IReadOnlyList<DocumentFolderResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<DocumentFolderResponse>> result = await handler.HandleAsync(new GetDocumentFoldersByProjectQuery(projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);

        RouteGroupBuilder stages = app.MapGroup("/api/project-stages/{stageId:guid}").WithTags("DocumentFolders");

        stages.MapPost("/document-folders", async (
            Guid stageId, CreateStageFolderRequest request, ICommandHandler<CreateStageFolderCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateStageFolderCommand(stageId, request.Name, request.ParentFolderId), ct);
            return result.Match(id => Results.Ok(new { FolderId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload);

        stages.MapGet("/document-folders", async (
            Guid stageId, IQueryHandler<GetDocumentFoldersByStageQuery, IReadOnlyList<DocumentFolderResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<DocumentFolderResponse>> result = await handler.HandleAsync(new GetDocumentFoldersByStageQuery(stageId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);

        RouteGroupBuilder tasks = app.MapGroup("/api/project-tasks/{taskId:guid}").WithTags("DocumentFolders");

        tasks.MapPost("/document-folders", async (
            Guid taskId, CreateTaskFolderRequest request, ICommandHandler<CreateTaskFolderCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateTaskFolderCommand(taskId, request.Name, request.ParentFolderId), ct);
            return result.Match(id => Results.Ok(new { FolderId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload);

        tasks.MapGet("/document-folders", async (
            Guid taskId, IQueryHandler<GetDocumentFoldersByTaskQuery, IReadOnlyList<DocumentFolderResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<DocumentFolderResponse>> result = await handler.HandleAsync(new GetDocumentFoldersByTaskQuery(taskId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);
    }

    private sealed record RenameFolderRequest(string Name);
    private sealed record MoveFolderRequest(Guid? NewParentFolderId);
    private sealed record CreateStageFolderRequest(string Name, Guid? ParentFolderId = null);
    private sealed record CreateTaskFolderRequest(string Name, Guid? ParentFolderId = null);
}
