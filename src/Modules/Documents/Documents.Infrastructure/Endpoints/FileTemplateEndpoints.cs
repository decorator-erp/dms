using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Documents.Contracts;
using Documents.Contracts.FileTemplates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Documents.Infrastructure.Endpoints;

/// <summary>File template CRUD (G7-06), governed by <see cref="DocumentsPermissions.FileTemplatesRead"/>/
/// <see cref="DocumentsPermissions.FileTemplatesManage"/> — the separate permission pair Documents
/// carries for this resource, unlike folders which reuse the document permissions.</summary>
internal sealed class FileTemplateEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder templates = app.MapGroup("/api/file-templates").WithTags("FileTemplates");

        templates.MapPost("/", async (
            IFormFile file, [FromForm] string name, [FromForm] string category, [FromForm] string? description,
            ICommandHandler<CreateFileTemplateCommand, Guid> handler, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A non-empty file is required.");
            }

            await using Stream stream = file.OpenReadStream();
            Result<Guid> result = await handler.HandleAsync(new CreateFileTemplateCommand(
                name, category, stream, file.FileName, file.ContentType, file.Length, description), ct);
            return result.Match(id => Results.Ok(new { FileTemplateId = id }), r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.FileTemplatesManage).DisableAntiforgery();

        templates.MapGet("/", async (
            string? category, IQueryHandler<GetFileTemplatesQuery, IReadOnlyList<FileTemplateResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<FileTemplateResponse>> result = await handler.HandleAsync(new GetFileTemplatesQuery(category), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.FileTemplatesRead);

        templates.MapGet("/{fileTemplateId:guid}", async (
            Guid fileTemplateId, IQueryHandler<GetFileTemplateByIdQuery, FileTemplateResponse> handler, CancellationToken ct) =>
        {
            Result<FileTemplateResponse> result = await handler.HandleAsync(new GetFileTemplateByIdQuery(fileTemplateId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.FileTemplatesRead);

        templates.MapPut("/{fileTemplateId:guid}", async (
            Guid fileTemplateId, UpdateFileTemplateRequest request, ICommandHandler<UpdateFileTemplateCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateFileTemplateCommand(fileTemplateId, request.Name, request.Category, request.Description), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.FileTemplatesManage);

        templates.MapDelete("/{fileTemplateId:guid}", async (
            Guid fileTemplateId, ICommandHandler<DeleteFileTemplateCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteFileTemplateCommand(fileTemplateId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.FileTemplatesManage);

        templates.MapPost("/{fileTemplateId:guid}/activate", async (
            Guid fileTemplateId, ICommandHandler<ActivateFileTemplateCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ActivateFileTemplateCommand(fileTemplateId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.FileTemplatesManage);

        templates.MapPost("/{fileTemplateId:guid}/deactivate", async (
            Guid fileTemplateId, ICommandHandler<DeactivateFileTemplateCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeactivateFileTemplateCommand(fileTemplateId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.FileTemplatesManage);
    }

    private sealed record UpdateFileTemplateRequest(string Name, string Category, string? Description);
}
