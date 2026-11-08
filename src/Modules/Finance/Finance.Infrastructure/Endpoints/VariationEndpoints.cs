using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.Variations;
using Hr.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// Variations — twelve routes, and the only surface in the module using four different permissions.
///
/// <para><b>The split is the point.</b> <c>finance.manage_variation</c> is the AUTHORING bucket: raise,
/// submit, reject, cancel. <c>finance.variations.approve</c>, <c>.sign</c> and <c>.apply</c> are three
/// separately grantable sensitive transitions — approving commits the office, signing commits the
/// client, and applying moves money into a budget line. A role may hold authoring without any of the
/// three, which is exactly the segregation the backend's own comment describes.</para>
///
/// <para><c>approve</c> and <c>sign</c> reach the same domain transition
/// (<c>Variation.Approve</c>) and differ only in whether the signature arguments are mandatory —
/// two routes because they carry different authority, one transition because a second would be a second
/// place for the signature rules to drift.</para>
/// </summary>
internal sealed class VariationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/variations").WithTags("Variations");

        group.MapPost("/", async (
            RaiseVariationRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<RaiseVariationCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> requestedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (requestedById.IsFailure)
            {
                return requestedById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(
                new RaiseVariationCommand(request, requestedById.Value), ct);

            return result.Match(
                id => Results.Created($"/api/variations/{id}", new { VariationId = id }),
                r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageVariation);

        group.MapPut("/{id:guid}/submit", async (
            Guid id, ICommandHandler<SubmitVariationCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SubmitVariationCommand(id), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageVariation);

        group.MapPut("/{id:guid}/description", async (
            Guid id, UpdateVariationDescriptionRequest request,
            ICommandHandler<UpdateVariationDescriptionCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateVariationDescriptionCommand(id, request.Description), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageVariation);

        group.MapPut("/{id:guid}/approve", async (
            Guid id, ApproveVariationRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<ApproveVariationCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> approvedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (approvedById.IsFailure)
            {
                return approvedById.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new ApproveVariationCommand(
                    id, approvedById.Value, request.SignedByClientId, request.SignedDocumentPath),
                ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Variations.Approve);

        group.MapPut("/{id:guid}/sign", async (
            Guid id, SignVariationRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<SignVariationCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> approvedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (approvedById.IsFailure)
            {
                return approvedById.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new SignVariationCommand(
                    id, approvedById.Value, request.SignedByClientId, request.SignedDocumentPath),
                ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Variations.Sign);

        group.MapPut("/{id:guid}/apply", async (
            Guid id, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<ApplyVariationCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> recordedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (recordedById.IsFailure)
            {
                return recordedById.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new ApplyVariationCommand(id, recordedById.Value), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Variations.Apply);

        group.MapPut("/{id:guid}/reject", async (
            Guid id, RejectVariationRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<RejectVariationCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> rejectedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (rejectedById.IsFailure)
            {
                return rejectedById.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new RejectVariationCommand(id, rejectedById.Value, request.Note), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageVariation);

        group.MapPut("/{id:guid}/cancel", async (
            Guid id, CancelVariationRequest request,
            ICommandHandler<CancelVariationCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new CancelVariationCommand(id, request.Note), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageVariation);

        group.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetVariationByIdQuery, VariationResponse> handler,
            CancellationToken ct) =>
        {
            Result<VariationResponse> result = await handler.HandleAsync(new GetVariationByIdQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}", async (
            Guid projectId, VariationKind? kind, VariationStatus? status, int? skip, int? take,
            IQueryHandler<GetVariationsByProjectQuery, PagedResult<VariationListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<VariationListItemResponse>> result = await handler.HandleAsync(
                new GetVariationsByProjectQuery(projectId, kind, status, skip ?? 0, take ?? 50), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-stage/{stageId:guid}", async (
            Guid stageId,
            IQueryHandler<GetVariationsByStageQuery, IReadOnlyList<VariationListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<VariationListItemResponse>> result = await handler.HandleAsync(new GetVariationsByStageQuery(stageId), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-task/{taskId:guid}", async (
            Guid taskId,
            IQueryHandler<GetVariationsByTaskQuery, IReadOnlyList<VariationListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<VariationListItemResponse>> result = await handler.HandleAsync(new GetVariationsByTaskQuery(taskId), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);
    }
}
