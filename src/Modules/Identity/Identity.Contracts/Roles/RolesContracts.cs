using BuildingBlocks.Messaging;

namespace Identity.Contracts.Roles;

/// <summary>See the remarks on <c>Identity.Contracts.Auth.AuthContracts</c> — same reason these command/
/// query wrapper types and DTOs live here rather than in <c>Identity.Application</c>.</summary>
public sealed record RoleResponse(string Id, string Name, string? Description, bool IsActive);

public sealed record CreateRoleCommand(string Name) : ICommand<string>;

public sealed record UpdateRoleCommand(string RoleId, string Name, string? Description) : ICommand;

public sealed record DeleteRoleCommand(string RoleId) : ICommand;

public sealed record GetRoleQuery(string RoleId) : IQuery<RoleResponse>;

public sealed record GetAllRolesQuery : IQuery<IReadOnlyList<RoleResponse>>;

/// <summary>
/// The service methods behind these three existed already — <c>IRoleService.AssignRoleToUserAsync</c>,
/// <c>RemoveRoleFromUserAsync</c>, <c>GetUserRolesAsync</c> — reachable from no route. A user's roles
/// could never be changed through the API at all.
/// </summary>
public sealed record AssignRoleToUserCommand(string UserId, string RoleId) : ICommand;

public sealed record RemoveRoleFromUserCommand(string UserId, string RoleId) : ICommand;

public sealed record GetUserRolesQuery(string UserId) : IQuery<IReadOnlyList<RoleResponse>>;
