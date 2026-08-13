using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Documents.Infrastructure.Endpoints;

/// <summary>Its own file, split from <see cref="DocumentEndpoints"/> — the only route in the module that
/// returns a streamed response body rather than JSON. <c>Results.Stream</c> writes the file's own open
/// <see cref="Stream"/> straight to the response body as it reads, never buffering the whole file into a
/// byte array first, matching the same "streaming rather than buffering" concern the plan raises for
/// upload.</summary>
internal sealed class DocumentDownloadEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/project-documents/{documentId:guid}/download", async (
            Guid documentId, IQueryHandler<GetProjectDocumentDownloadQuery, ProjectDocumentDownloadResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectDocumentDownloadQuery(documentId), ct);
            return result.Match(
                response => Results.Stream(response.Content, response.ContentType, response.FileName),
                r => r.ToProblem());
        }).RequirePermission(DocumentsPermissions.Read).WithTags("Documents");
    }
}
