using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.DesignProfile;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Office-facing read of a client's accumulated design preferences, gated by
/// <see cref="CrmPermissions.DesignProfile"/>. Deliberately excludes budget — see
/// <c>ClientDesignProfileResponse</c>'s own remarks.</summary>
internal sealed class CrmDesignProfileEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapGet("/api/crm/projects/{projectId:guid}/design-profile", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetClientDesignProfileQuery, ClientDesignProfileResponse> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<ClientDesignProfileResponse> result = await handler.HandleAsync(new GetClientDesignProfileQuery(projectId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).WithTags("Crm Design Profile").RequirePermission(CrmPermissions.DesignProfile.Read);
}
