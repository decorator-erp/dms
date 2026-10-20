using BuildingBlocks.Pagination;
using Identity.Contracts.Users;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Users;

/// <summary>
/// N6: the shared page read behind <c>GET /api/users</c> and <c>GET /api/roles/{roleId}/users</c>. Two
/// statements per page regardless of its size (B11): the count, then the page with every user's role
/// names gathered by one join.
///
/// <para>The count and the page are separate statements, so a user created or deleted between them can
/// make the total disagree with the rows by one. Accepted: the next page read corrects it.</para>
/// </summary>
internal static class UserListing
{
    public static async Task<PagedResult<UserListItem>> PageAsync(
        IdentityModuleDbContext db, IQueryable<ApplicationUser> users, int pageNumber, int pageSize,
        CancellationToken ct)
    {
        int total = await users.CountAsync(ct);
        if (!PageWindow.TrySkip(pageNumber, pageSize, out int skip))
        {
            return new PagedResult<UserListItem>([], pageNumber, pageSize, total);
        }

        var rows = await users
            .OrderBy(u => u.Email)
            .ThenBy(u => u.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.UserType,
                u.IsActive,
                u.LastLoginAt,
                Roles = (
                    from ur in db.UserRoles
                    join r in db.Roles on ur.RoleId equals r.Id
                    where ur.UserId == u.Id
                    orderby r.Name
                    select r.Name!).ToList(),
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return new PagedResult<UserListItem>(
            [.. rows.Select(r => new UserListItem(
                r.Id, r.Email ?? string.Empty, r.FirstName, r.LastName, r.UserType.ToString(), r.IsActive,
                r.LastLoginAt, r.Roles))],
            pageNumber,
            pageSize,
            total);
    }
}
