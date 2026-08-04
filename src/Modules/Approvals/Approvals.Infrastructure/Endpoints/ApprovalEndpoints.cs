using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approvals.Infrastructure.Endpoints;

/// <summary>Ported route shapes from <c>../backend/API/Endpoints/ApprovalEndpoints.cs</c>, minus the
/// admin-override computation (dropped per decision #2/R-B — <c>DecideAny</c> is checked inside the
/// handler itself, not passed in from the endpoint).</summary>
internal sealed class ApprovalEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approvals").WithTags("Approvals");

        group.MapGet("/pending", async (
            int pageNumber, int pageSize,
            IQueryHandler<GetMyPendingApprovalsQuery, PagedResult<PendingApprovalResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetMyPendingApprovalsQuery(pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ApprovalsPermissions.Read);

        group.MapGet("/by-subject/{subjectType}/{subjectId:guid}", async (
            ApprovalSubjectType subjectType, Guid subjectId,
            IQueryHandler<GetApprovalsBySubjectQuery, IReadOnlyList<ApprovalTrailItemResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetApprovalsBySubjectQuery(subjectType, subjectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ApprovalsPermissions.Read);

        group.MapPut("/{approvalId:guid}/decide", async (
            Guid approvalId, DecideApprovalRequest request,
            ICommandHandler<DecideApprovalCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new DecideApprovalCommand(approvalId, request.Decision, request.Comment), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ApprovalsPermissions.Decide);
    }

    private sealed record DecideApprovalRequest(ApprovalDecision Decision, string? Comment);
}
