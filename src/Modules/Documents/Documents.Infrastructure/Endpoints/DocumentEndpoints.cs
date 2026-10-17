using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Documents.Infrastructure.Endpoints;

/// <summary>Document CRUD/queries that don't touch file content — uploads live in
/// <see cref="DocumentUploadEndpoints"/>, download in <see cref="DocumentDownloadEndpoints"/>, both
/// distinct concerns from plain routes returning/mutating a <see cref="ProjectDocumentResponse"/>
/// row.</summary>
internal sealed class DocumentEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder documents = app.MapGroup("/api/project-documents").WithTags("Documents");

        documents.MapGet("/{documentId:guid}", async (
            Guid documentId, IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse> handler, CancellationToken ct) =>
        {
            Result<ProjectDocumentResponse> result = await handler.HandleAsync(new GetProjectDocumentByIdQuery(documentId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);

        documents.MapPut("/{documentId:guid}/move", async (
            Guid documentId, MoveDocumentRequest request, ICommandHandler<MoveProjectDocumentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new MoveProjectDocumentCommand(documentId, request.FolderId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Update);

        documents.MapDelete("/{documentId:guid}", async (
            Guid documentId, ICommandHandler<DeleteProjectDocumentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteProjectDocumentCommand(documentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Delete);

        documents.MapPost("/{documentId:guid}/request-review", async (
            Guid documentId, RequestReviewRequest request, ICurrentUser currentUser,
            ICommandHandler<RequestDocumentReviewCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(
                new RequestDocumentReviewCommand(documentId, request.ApproverEmployeeId, currentUser.UserId!), ct);
            return result.Match(id => Results.Ok(new { ApprovalId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Update);

        RouteGroupBuilder projects = app.MapGroup("/api/projects/{projectId:guid}").WithTags("Documents");

        projects.MapGet("/documents", async (
            Guid projectId, IQueryHandler<GetProjectDocumentsByProjectQuery, IReadOnlyList<ProjectDocumentResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectDocumentResponse>> result = await handler.HandleAsync(new GetProjectDocumentsByProjectQuery(projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);

        RouteGroupBuilder folders = app.MapGroup("/api/document-folders/{folderId:guid}").WithTags("Documents");

        folders.MapGet("/documents", async (
            Guid folderId, IQueryHandler<GetProjectDocumentsByFolderQuery, IReadOnlyList<ProjectDocumentResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectDocumentResponse>> result = await handler.HandleAsync(new GetProjectDocumentsByFolderQuery(folderId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);

        RouteGroupBuilder stages = app.MapGroup("/api/project-stages/{stageId:guid}").WithTags("Documents");

        stages.MapGet("/documents", async (
            Guid stageId, IQueryHandler<GetProjectDocumentsByStageQuery, IReadOnlyList<ProjectDocumentResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectDocumentResponse>> result = await handler.HandleAsync(new GetProjectDocumentsByStageQuery(stageId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);

        RouteGroupBuilder tasks = app.MapGroup("/api/project-tasks/{taskId:guid}").WithTags("Documents");

        tasks.MapGet("/documents", async (
            Guid taskId, IQueryHandler<GetProjectDocumentsByTaskQuery, IReadOnlyList<ProjectDocumentResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectDocumentResponse>> result = await handler.HandleAsync(new GetProjectDocumentsByTaskQuery(taskId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read);
    }

    private sealed record MoveDocumentRequest(Guid? FolderId);
    private sealed record RequestReviewRequest(Guid ApproverEmployeeId);
}
