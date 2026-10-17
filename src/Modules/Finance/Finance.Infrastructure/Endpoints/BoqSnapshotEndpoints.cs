using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.BoqSnapshots;
using Hr.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// BOQ snapshots — four routes, and <b>no update and no delete, by design</b>. A snapshot is a statement
/// about a moment; correcting one means generating the next version. That absence is asserted by
/// <c>NoUpdateOrDeleteCommandExistsForBoqSnapshots</c> rather than left as prose, so adding one is a
/// deliberate decision rather than an accident.
/// </summary>
internal sealed class BoqSnapshotEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/boq-snapshots").WithTags("BoqSnapshots");

        group.MapPost("/", async (
            GenerateBoqSnapshotRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<GenerateBoqSnapshotCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> generatedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (generatedById.IsFailure)
            {
                return generatedById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(
                new GenerateBoqSnapshotCommand(request, generatedById.Value), ct);

            return result.Match(
                id => Results.Created($"/api/boq-snapshots/{id}", new { BoqSnapshotId = id }),
                r => r.ToProblem());
        }).RequirePermission(FinancePermissions.GenerateBoq);

        group.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetBoqSnapshotByIdQuery, BoqSnapshotResponse> handler,
            CancellationToken ct) =>
        {
            Result<BoqSnapshotResponse> result = await handler.HandleAsync(new GetBoqSnapshotByIdQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}", async (
            Guid projectId, BoqSnapshotType? type, int? skip, int? take,
            IQueryHandler<GetBoqSnapshotsByProjectQuery, PagedResult<BoqSnapshotListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<BoqSnapshotListItemResponse>> result = await handler.HandleAsync(
                new GetBoqSnapshotsByProjectQuery(projectId, type, skip ?? 0, take ?? 50), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}/latest", async (
            Guid projectId, Guid? projectStageId,
            IQueryHandler<GetLatestBoqSnapshotQuery, BoqSnapshotResponse> handler,
            CancellationToken ct) =>
        {
            Result<BoqSnapshotResponse> result = await handler.HandleAsync(
                new GetLatestBoqSnapshotQuery(projectId, projectStageId), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);
    }
}
