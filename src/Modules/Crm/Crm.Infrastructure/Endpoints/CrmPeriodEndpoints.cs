using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Periods;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Requirement period open/extend/close, governed by <see cref="CrmPermissions.Periods"/>.
/// <see cref="OpenCrmRequirementPeriodCommand.OpenedById"/> is resolved from the caller via
/// <see cref="CrmEndpointActors.ResolveEmployeeIdAsync"/>.</summary>
internal sealed class CrmPeriodEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder byProject = app.MapGroup("/api/crm/projects/{projectId:guid}/periods").WithTags("Crm Periods");

        byProject.MapPost("/", async (
            Guid projectId, OpenPeriodRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<OpenCrmRequirementPeriodCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> openedById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (openedById.IsFailure)
            {
                return openedById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(
                new OpenCrmRequirementPeriodCommand(projectId, request.ProjectStageId, request.ClosesAtUtc, openedById.Value, currentUser.UserId!), ct);
            return result.Match(id => Results.Created($"/api/crm/periods/{id}", new { PeriodId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Open);

        byProject.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetCrmRequirementPeriodsByProjectQuery, IReadOnlyList<CrmRequirementPeriodResponse>> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmRequirementPeriodResponse>> result = await handler.HandleAsync(
                new GetCrmRequirementPeriodsByProjectQuery(projectId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Read);

        RouteGroupBuilder byId = app.MapGroup("/api/crm/periods/{periodId:guid}").WithTags("Crm Periods");

        byId.MapPut("/extend", async (
            Guid periodId, ExtendPeriodRequest request, ICurrentUser currentUser,
            ICommandHandler<ExtendCrmRequirementPeriodCommand> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new ExtendCrmRequirementPeriodCommand(periodId, request.NewClosesAtUtc, userId.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Extend);

        byId.MapPut("/close", async (
            Guid periodId, ICurrentUser currentUser, ICommandHandler<CloseCrmRequirementPeriodCommand> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new CloseCrmRequirementPeriodCommand(periodId, userId.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Close);

        byId.MapGet("/", async (
            Guid periodId, ICurrentUser currentUser,
            IQueryHandler<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<CrmRequirementPeriodResponse> result = await handler.HandleAsync(new GetCrmRequirementPeriodByIdQuery(periodId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Read);
    }

    private sealed record OpenPeriodRequest(DateTime ClosesAtUtc, Guid? ProjectStageId = null);
    private sealed record ExtendPeriodRequest(DateTime NewClosesAtUtc);
}
