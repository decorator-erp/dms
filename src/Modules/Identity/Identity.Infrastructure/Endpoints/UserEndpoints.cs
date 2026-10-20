using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Identity.Contracts;
using Identity.Contracts.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Identity.Infrastructure.Endpoints;

/// <summary>N6: <c>users.read</c> was in the catalog and gated nothing. This is the route it was for.</summary>
internal sealed class UserEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/", async (
            int? page, int? pageSize, string? search,
            IQueryHandler<GetUsersQuery, PagedResult<UserListItem>> handler, CancellationToken ct) =>
        {
            Result<PagedResult<UserListItem>> result = await handler.HandleAsync(
                new GetUsersQuery(page ?? 1, pageSize ?? 50, search), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.UsersRead);
    }
}
