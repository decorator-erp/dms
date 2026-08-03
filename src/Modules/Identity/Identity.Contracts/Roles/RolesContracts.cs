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
