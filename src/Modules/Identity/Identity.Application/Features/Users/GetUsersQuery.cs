using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Persistence;
using FluentValidation;
using Identity.Contracts.Users;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Users;

internal sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(256);
    }
}

/// <summary>N6: <c>GET /api/users</c>. Includes clients and the seeded admin by design: it is the list
/// an administrator manages accounts from. The search is literal (<see cref="Like"/>) over email, first
/// and last name.</summary>
internal sealed class GetUsersQueryHandler(IdentityModuleDbContext db)
    : IQueryHandler<GetUsersQuery, PagedResult<UserListItem>>
{
    public async Task<Result<PagedResult<UserListItem>>> HandleAsync(GetUsersQuery query, CancellationToken ct = default)
    {
        IQueryable<ApplicationUser> users = db.Users;

        if (Like.Contains(query.Search) is { } pattern)
        {
            users = users.Where(u =>
                EF.Functions.ILike(u.Email!, pattern, Like.EscapeCharacter)
                || EF.Functions.ILike(u.FirstName, pattern, Like.EscapeCharacter)
                || EF.Functions.ILike(u.LastName, pattern, Like.EscapeCharacter));
        }

        return await UserListing.PageAsync(db, users, query.PageNumber, query.PageSize, ct);
    }
}
