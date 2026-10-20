using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Identity.Contracts.Users;

/// <summary>N6: a row of the user administration list. <c>Roles</c> are role names.</summary>
public sealed record UserListItem(
    string Id,
    string Email,
    string FirstName,
    string LastName,
    string UserType,
    bool IsActive,
    DateTime? LastLoginAt,
    IReadOnlyList<string> Roles);

/// <summary>N6: every account, employees, clients and the seeded admin alike, paged and optionally
/// searched over email and names.</summary>
public sealed record GetUsersQuery(int PageNumber, int PageSize, string? Search = null)
    : IQuery<PagedResult<UserListItem>>;

/// <summary>N6: the members of one role, paged.</summary>
public sealed record GetRoleUsersQuery(string RoleId, int PageNumber, int PageSize)
    : IQuery<PagedResult<UserListItem>>;
