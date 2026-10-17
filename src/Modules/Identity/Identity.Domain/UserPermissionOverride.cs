using SharedKernel;

namespace Identity.Domain;

internal sealed class UserPermissionOverride : Entity, IAuditable
{
    private UserPermissionOverride() { }

    private UserPermissionOverride(
        string userId, string permission, bool isGranted, string grantedById, string? reason, DateTime? expiresAt)
    {
        UserId = userId;
        Permission = permission;
        IsGranted = isGranted;
        GrantedById = grantedById;
        Reason = reason;
        ExpiresAt = expiresAt;
    }
    public string UserId { get; private set; } = null!;
    public string Permission { get; private set; } = null!;
    public string? Reason { get; private set; }
    public bool IsGranted { get; private set; }
    public string GrantedById { get; private set; } = null!;
    public DateTime? ExpiresAt { get; private set; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    public static UserPermissionOverride Grant(
        string userId, string permission, string grantedById, string? reason = null, DateTime? expiresAt = null) =>
        new(userId, permission, true, grantedById, reason, expiresAt);

    public static UserPermissionOverride Deny(
        string userId, string permission, string grantedById, string? reason = null, DateTime? expiresAt = null) =>
        new(userId, permission, false, grantedById, reason, expiresAt);
}
