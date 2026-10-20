using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Identity.Contracts.Users;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Users;

internal sealed class GetRoleUsersQueryValidator : AbstractValidator<GetRoleUsersQuery>
{
    public GetRoleUsersQueryValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>N6: <c>GET /api/roles/{roleId}/users</c>. An unknown role is a 404, not an empty page.</summary>
internal sealed class GetRoleUsersQueryHandler(IdentityModuleDbContext db)
    : IQueryHandler<GetRoleUsersQuery, PagedResult<UserListItem>>
{
    public async Task<Result<PagedResult<UserListItem>>> HandleAsync(
        GetRoleUsersQuery query, CancellationToken ct = default)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == query.RoleId, ct))
        {
            return RoleErrors.NotFound;
        }

        IQueryable<ApplicationUser> members = db.Users
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == query.RoleId));

        return await UserListing.PageAsync(db, members, query.PageNumber, query.PageSize, ct);
    }
}
