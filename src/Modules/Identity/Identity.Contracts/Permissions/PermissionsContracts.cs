using BuildingBlocks.Messaging;

namespace Identity.Contracts.Permissions;

public sealed record PermissionResponse(
    string ClaimType, string ClaimValue, string Group, string DisplayName, string? Description);
public sealed record EffectivePermissionsResponse(
    string UserId, IReadOnlyList<string> Permissions, IReadOnlyList<PermissionOverrideResponse> Overrides);
public sealed record PermissionOverrideResponse(
    Guid Id, string Permission, bool IsGranted, string GrantedById, string? Reason, DateTime? ExpiresAt);

public sealed record RevokePermissionOverrideCommand(string TargetUserId, string Permission) : ICommand;

public sealed record AssignPermissionToRoleCommand(string RoleId, string Permission) : ICommand;

public sealed record RemovePermissionFromRoleCommand(string RoleId, string Permission) : ICommand;

public sealed record GrantPermissionOverrideCommand(
    string TargetUserId, string Permission, string? Reason = null, DateTime? ExpiresAtUtc = null) : ICommand;

public sealed record DenyPermissionOverrideCommand(
    string TargetUserId, string Permission, string? Reason = null, DateTime? ExpiresAtUtc = null) : ICommand;

public sealed record GetAllPermissionsQuery : IQuery<IReadOnlyList<ModulePermissionSet>>;

public sealed record GetUserPermissionsQuery(string UserId) : IQuery<IReadOnlyList<PermissionResponse>>;

public sealed record GetRolePermissionsQuery(string RoleId) : IQuery<IReadOnlyList<PermissionResponse>>;

public sealed record GetUserEffectivePermissionsQuery(string UserId) : IQuery<EffectivePermissionsResponse>;

