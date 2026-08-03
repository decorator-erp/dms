using BuildingBlocks.Messaging;

namespace Identity.Contracts.Permissions;

/// <summary>See the remarks on <c>Identity.Contracts.Auth.AuthContracts</c> — same reason these command/
/// query wrapper types and DTOs live here rather than in <c>Identity.Application</c>.</summary>
public sealed record PermissionResponse(
    string ClaimType, string ClaimValue, string Group, string DisplayName, string? Description);

public sealed record GetAllPermissionsQuery : IQuery<IReadOnlyList<ModulePermissionSet>>;

public sealed record GetUserPermissionsQuery(string UserId) : IQuery<IReadOnlyList<PermissionResponse>>;

public sealed record GetRolePermissionsQuery(string RoleId) : IQuery<IReadOnlyList<PermissionResponse>>;

public sealed record AssignPermissionToRoleCommand(string RoleId, string Permission) : ICommand;

public sealed record RemovePermissionFromRoleCommand(string RoleId, string Permission) : ICommand;

public sealed record EffectivePermissionsResponse(
    string UserId, IReadOnlyList<string> Permissions, IReadOnlyList<PermissionOverrideResponse> Overrides);

public sealed record PermissionOverrideResponse(
    Guid Id, string Permission, bool IsGranted, string GrantedById, string? Reason, DateTime? ExpiresAt);

public sealed record GetUserEffectivePermissionsQuery(string UserId) : IQuery<EffectivePermissionsResponse>;

public sealed record GrantPermissionOverrideCommand(
    string TargetUserId, string Permission, string? Reason = null, DateTime? ExpiresAtUtc = null) : ICommand;

public sealed record DenyPermissionOverrideCommand(
    string TargetUserId, string Permission, string? Reason = null, DateTime? ExpiresAtUtc = null) : ICommand;

public sealed record RevokePermissionOverrideCommand(string TargetUserId, string Permission) : ICommand;
