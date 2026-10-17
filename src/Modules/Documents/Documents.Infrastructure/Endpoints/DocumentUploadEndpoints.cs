using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Documents.Infrastructure.Endpoints;

/// <summary>
/// The five upload-shaped routes (G7-07/G7-08), split from <see cref="DocumentEndpoints"/> because every
/// one of them takes multipart content (or, for the template-apply route, copies server-side bytes) —
/// a distinct concern from the plain CRUD/query routes. <c>IFormFile</c> binding matches
/// <c>../backend/API/Endpoints</c>'s own proven pattern; <see cref="DocumentsModule"/> configures
/// <c>FormOptions.MultipartBodyLengthLimit</c> from <c>FileStorage:MaxUploadBytes</c> so oversized
/// multipart bodies are rejected before a form value provider spends time parsing them, and ASP.NET
/// Core's own multipart handling spills a file section to a temp file once past its (small) in-memory
/// threshold rather than holding it fully in RAM — the "streaming rather than buffering" hardening the
/// plan calls for. <see cref="Services.LocalFileStorageService.SaveAsync"/>'s own chunk-by-chunk cap
/// enforcement (G7-05) is the authoritative backstop regardless of what the HTTP layer allows through.
/// </summary>
internal sealed class DocumentUploadEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder projects = app.MapGroup("/api/projects/{projectId:guid}").WithTags("Documents");

        projects.MapPost("/documents", async (
            Guid projectId, IFormFile file, [FromForm] Guid? folderId, [FromForm] string? description,
            ICurrentUser currentUser, ICommandHandler<UploadProjectDocumentCommand, Guid> handler, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A non-empty file is required.");
            }

            await using Stream stream = file.OpenReadStream();
            Result<Guid> result = await handler.HandleAsync(new UploadProjectDocumentCommand(
                projectId, stream, file.FileName, file.ContentType, file.Length, currentUser.UserId!, folderId, description), ct);
            return result.Match(id => Results.Ok(new { DocumentId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload).DisableAntiforgery();

        RouteGroupBuilder stages = app.MapGroup("/api/project-stages/{stageId:guid}").WithTags("Documents");

        stages.MapPost("/documents", async (
            Guid stageId, IFormFile file, [FromForm] Guid? folderId, [FromForm] string? description,
            ICurrentUser currentUser, ICommandHandler<UploadStageDocumentCommand, Guid> handler, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A non-empty file is required.");
            }

            await using Stream stream = file.OpenReadStream();
            Result<Guid> result = await handler.HandleAsync(new UploadStageDocumentCommand(
                stageId, stream, file.FileName, file.ContentType, file.Length, currentUser.UserId!, folderId, description), ct);
            return result.Match(id => Results.Ok(new { DocumentId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload).DisableAntiforgery();

        RouteGroupBuilder tasks = app.MapGroup("/api/project-tasks/{taskId:guid}").WithTags("Documents");

        tasks.MapPost("/documents", async (
            Guid taskId, IFormFile file, [FromForm] Guid? folderId, [FromForm] string? description,
            ICurrentUser currentUser, ICommandHandler<UploadTaskDocumentCommand, Guid> handler, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A non-empty file is required.");
            }

            await using Stream stream = file.OpenReadStream();
            Result<Guid> result = await handler.HandleAsync(new UploadTaskDocumentCommand(
                taskId, stream, file.FileName, file.ContentType, file.Length, currentUser.UserId!, folderId, description), ct);
            return result.Match(id => Results.Ok(new { DocumentId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload).DisableAntiforgery();

        tasks.MapPost("/apply-template/{fileTemplateId:guid}", async (
            Guid taskId, Guid fileTemplateId, ICurrentUser currentUser,
            ICommandHandler<ApplyFileTemplateToTaskCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(
                new ApplyFileTemplateToTaskCommand(taskId, fileTemplateId, currentUser.UserId!), ct);
            return result.Match(id => Results.Ok(new { DocumentId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload);

        RouteGroupBuilder documents = app.MapGroup("/api/project-documents").WithTags("Documents");

        documents.MapPost("/{documentId:guid}/revisions", async (
            Guid documentId, IFormFile file, [FromForm] string? description,
            ICurrentUser currentUser, ICommandHandler<UploadDocumentRevisionCommand, Guid> handler, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A non-empty file is required.");
            }

            await using Stream stream = file.OpenReadStream();
            Result<Guid> result = await handler.HandleAsync(new UploadDocumentRevisionCommand(
                documentId, stream, file.FileName, file.ContentType, file.Length, currentUser.UserId!, description), ct);
            return result.Match(id => Results.Ok(new { DocumentId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Upload).DisableAntiforgery();
    }
}
