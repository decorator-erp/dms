using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.MyWork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>crm-complete F3 (D5): the calling employee's gathering work. <c>crm.briefs.read</c>.</summary>
internal sealed class CrmMyWorkEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/crm/my-gathering-work", async (
            ICurrentUser currentUser, IQueryHandler<GetMyGatheringWorkQuery, IReadOnlyList<MyGatheringWorkItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<MyGatheringWorkItemResponse>> result = await handler.HandleAsync(new GetMyGatheringWorkQuery(currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).WithTags("Crm My Work").RequirePermission(CrmPermissions.Briefs.Read);
}
