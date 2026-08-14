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

namespace Crm.Infrastructure.Endpoints;

/// <summary>Requirement period open/extend/close, governed by <see cref="CrmPermissions.Periods"/>.
/// <see cref="OpenCrmRequirementPeriodCommand.OpenedById"/> is resolved from the caller via
/// <see cref="CrmEndpointActors.ResolveEmployeeIdAsync"/>.</summary>
internal sealed class CrmPeriodEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var byProject = app.MapGroup("/api/crm/projects/{projectId:guid}/periods").WithTags("Crm Periods");

        byProject.MapPost("/", async (
            Guid projectId, OpenPeriodRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<OpenCrmRequirementPeriodCommand, Guid> handler, CancellationToken ct) =>
        {
            var openedById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (openedById.IsFailure)
                return openedById.ToProblem();

            var result = await handler.HandleAsync(
                new OpenCrmRequirementPeriodCommand(projectId, request.ProjectStageId, request.ClosesAtUtc, openedById.Value), ct);
            return result.Match(id => Results.Created($"/api/crm/periods/{id}", new { PeriodId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Open);

        byProject.MapGet("/", async (
            Guid projectId, IQueryHandler<GetCrmRequirementPeriodsByProjectQuery, IReadOnlyList<CrmRequirementPeriodResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCrmRequirementPeriodsByProjectQuery(projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Read);

        var byId = app.MapGroup("/api/crm/periods/{periodId:guid}").WithTags("Crm Periods");

        byId.MapPut("/extend", async (
            Guid periodId, ExtendPeriodRequest request, ICommandHandler<ExtendCrmRequirementPeriodCommand> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ExtendCrmRequirementPeriodCommand(periodId, request.NewClosesAtUtc), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Extend);

        byId.MapPut("/close", async (
            Guid periodId, ICommandHandler<CloseCrmRequirementPeriodCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CloseCrmRequirementPeriodCommand(periodId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Close);

        byId.MapGet("/", async (
            Guid periodId, IQueryHandler<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCrmRequirementPeriodByIdQuery(periodId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Periods.Read);
    }

    private sealed record OpenPeriodRequest(DateTime ClosesAtUtc, Guid? ProjectStageId = null);
    private sealed record ExtendPeriodRequest(DateTime NewClosesAtUtc);
}
