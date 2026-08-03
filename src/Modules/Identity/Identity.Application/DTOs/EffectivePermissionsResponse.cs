namespace Identity.Application.DTOs;

internal sealed record EffectivePermissionsResponse(
    string UserId, IReadOnlyList<string> Permissions, IReadOnlyList<PermissionOverrideResponse> Overrides);

internal sealed record PermissionOverrideResponse(
    Guid Id, string Permission, bool IsGranted, string GrantedById, string? Reason, DateTime? ExpiresAt);
