using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Hr.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Documents;
using Sites.Contracts.Records;
using Sites.Contracts.Surveys;

namespace Sites.Infrastructure.Endpoints;

/// <summary>
/// The survey surface. <c>sites.read</c> says a caller may read site data, not that they may read
/// <i>this</i> site's data — row-level access is <c>SiteScopeDecorator</c>'s job, and every route below
/// whose command or query carries a <c>SiteId</c> gets it automatically.
///
/// <para><c>sites.approve</c> and <c>sites.delete</c> are office-only: the backend's Employee role holds
/// read/create/update/submit and deliberately not those two, because a surveyor who could approve their
/// own submission would make the Draft → Submitted → Approved lifecycle decorative on day one.</para>
///
/// <para><b>Not ported: <c>GET /{siteId}/variance</c></b> (S1-01, [D8]). It diffs a survey against its
/// source CRM brief, and nothing in this system writes <c>Site.SourceBriefId</c>, so the route could
/// only ever answer "no source brief". It is also the only route that would have needed a second
/// module's permission (<c>crm.briefs.read</c>) stacked on its own.</para>
/// </summary>
internal sealed class SiteEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder sites = app.MapGroup("/api/sites").WithTags("Sites");

        // projectId is REQUIRED, not optional. A Guid cannot carry a metadata default, so making it
        // optional throws "Argument types do not match" at route enumeration -- at startup, not on the
        // first request. Do not "improve" this into a nullable.
        sites.MapGet("/", async (
            // projectId stays a bare Guid on purpose (see below); the paging pair is nullable because a
            // non-nullable query parameter is REQUIRED, and paging must have working defaults.
            Guid projectId, SiteStatus? status, int? pageIndex, int? pageSize,
            ICurrentUser currentUser,
            IQueryHandler<GetSitesQuery, PagedResult<SiteListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<SiteListItemResponse>> result = await handler.HandleAsync(
                new GetSitesQuery(
                    projectId, status, currentUser.UserId ?? string.Empty,
                    pageIndex ?? 1, pageSize ?? 25),
                ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        sites.MapPost("/", async (
            CreateSiteRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<CreateSiteCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> createdById = await SiteEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (createdById.IsFailure)
            {
                return createdById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(
                new CreateSiteCommand(request, createdById.Value, currentUser.UserId ?? string.Empty), ct);

            return result.Match(
                id => Results.Created($"/api/sites/{id}", new { SiteId = id }), r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Create);

        RouteGroupBuilder site = app.MapGroup("/api/sites/{siteId:guid}").WithTags("Sites");

        site.MapGet("/", async (
            Guid siteId, ICurrentUser currentUser,
            IQueryHandler<GetSiteByIdQuery, SiteDetailResponse> handler, CancellationToken ct) =>
        {
            Result<SiteDetailResponse> result = await handler.HandleAsync(
                new GetSiteByIdQuery(siteId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        site.MapPut("/", async (
            Guid siteId, UpdateSiteRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateSiteCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateSiteCommand(siteId, request, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        site.MapDelete("/", async (
            Guid siteId, ICurrentUser currentUser,
            ICommandHandler<DeleteSiteCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new DeleteSiteCommand(siteId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Delete);

        site.MapPost("/submit", async (
            Guid siteId, ICurrentUser currentUser,
            ICommandHandler<SubmitSiteCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new SubmitSiteCommand(siteId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Submit);

        site.MapPost("/approve", async (
            Guid siteId, DecideSiteRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<DecideSiteCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> decidedById = await SiteEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (decidedById.IsFailure)
            {
                return decidedById.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new DecideSiteCommand(
                    siteId, request, decidedById.Value, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Approve);

        // Reopen is sites.update, not sites.approve: reworking a rejected survey is the surveyor's job.
        site.MapPost("/reopen", async (
            Guid siteId, ICurrentUser currentUser,
            ICommandHandler<ReopenSiteCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new ReopenSiteCommand(siteId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        // ── Records ────────────────────────────────────────────────────────────

        site.MapGet("/records", async (
            Guid siteId, Guid moduleId, ICurrentUser currentUser,
            IQueryHandler<GetSitePivotQuery, SitePivotResponse> handler, CancellationToken ct) =>
        {
            Result<SitePivotResponse> result = await handler.HandleAsync(
                new GetSitePivotQuery(siteId, moduleId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        site.MapPost("/records", async (
            Guid siteId, CreateSiteRecordRequest request, ICurrentUser currentUser,
            ICommandHandler<AddSiteRecordCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(
                new AddSiteRecordCommand(siteId, request, currentUser.UserId ?? string.Empty), ct);

            return result.Match(
                id => Results.Created($"/api/sites/{siteId}/records/{id}", new { RecordId = id }),
                r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        site.MapPost("/records/bulk", async (
            Guid siteId, BulkSaveSiteRecordsRequest request, ICurrentUser currentUser,
            ICommandHandler<BulkSaveSiteRecordsCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new BulkSaveSiteRecordsCommand(siteId, request, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        site.MapGet("/records/grouped", async (
            Guid siteId, ICurrentUser currentUser,
            IQueryHandler<GetSiteRecordsGroupedQuery, SiteGroupedResponse> handler, CancellationToken ct) =>
        {
            Result<SiteGroupedResponse> result = await handler.HandleAsync(
                new GetSiteRecordsGroupedQuery(siteId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        site.MapPut("/records/{recordId:guid}", async (
            Guid siteId, Guid recordId, UpdateSiteRecordRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateSiteRecordCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateSiteRecordCommand(
                    siteId, recordId, request, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        site.MapDelete("/records/{recordId:guid}", async (
            Guid siteId, Guid recordId, ICurrentUser currentUser,
            ICommandHandler<DeleteSiteRecordCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new DeleteSiteRecordCommand(siteId, recordId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        site.MapPut("/records/{recordId:guid}/values", async (
            Guid siteId, Guid recordId, SetSiteRecordValuesRequest request, ICurrentUser currentUser,
            ICommandHandler<SetSiteRecordValuesCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new SetSiteRecordValuesCommand(
                    siteId, recordId, request, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        // ── Derived reads ──────────────────────────────────────────────────────

        site.MapGet("/families", async (
            Guid siteId, Guid moduleId, string? groupBy, ICurrentUser currentUser,
            IQueryHandler<GetSiteFamiliesQuery, SiteFamiliesResponse> handler, CancellationToken ct) =>
        {
            Result<SiteFamiliesResponse> result = await handler.HandleAsync(
                new GetSiteFamiliesQuery(siteId, moduleId, groupBy, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        site.MapGet("/boq", async (
            Guid siteId, ICurrentUser currentUser,
            IQueryHandler<GetSiteBoqQuery, SiteBoqResponse> handler, CancellationToken ct) =>
        {
            Result<SiteBoqResponse> result = await handler.HandleAsync(
                new GetSiteBoqQuery(siteId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        // ── Attachments ────────────────────────────────────────────────────────

        site.MapGet("/documents", async (
            Guid siteId, ICurrentUser currentUser,
            IQueryHandler<GetSiteDocumentsQuery, IReadOnlyList<SiteDocumentResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<SiteDocumentResponse>> result = await handler.HandleAsync(
                new GetSiteDocumentsQuery(siteId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Read);

        site.MapPost("/documents", async (
            Guid siteId, LinkSiteDocumentRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<LinkSiteDocumentCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> addedById = await SiteEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (addedById.IsFailure)
            {
                return addedById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(
                new LinkSiteDocumentCommand(
                    siteId, request, addedById.Value, currentUser.UserId ?? string.Empty), ct);

            return result.Match(
                id => Results.Created($"/api/sites/{siteId}/documents/{id}", new { DocumentLinkId = id }),
                r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);

        site.MapDelete("/documents/{documentLinkId:guid}", async (
            Guid siteId, Guid documentLinkId, ICurrentUser currentUser,
            ICommandHandler<UnlinkSiteDocumentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UnlinkSiteDocumentCommand(
                    siteId, documentLinkId, currentUser.UserId ?? string.Empty), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(SitesPermissions.Update);
    }
}
