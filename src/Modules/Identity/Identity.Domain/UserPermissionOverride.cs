using SharedKernel;

namespace Identity.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/UserPermissionOverride.cs</c>. Grant/deny precedence is
/// enforced by the unique <c>(UserId, Permission)</c> index at the persistence layer (G1-03), not by
/// code here — see <c>docs</c> notes ported alongside <c>EffectivePermissionService</c> at G1-04. A
/// user holds at most one override row per permission, so there is no "both a grant and a deny exist"
/// case for this entity to arbitrate.
/// </summary>
internal sealed class UserPermissionOverride : Entity, IAuditable
{
    public string UserId { get; private set; } = null!;
    public string Permission { get; private set; } = null!;
    public string? Reason { get; private set; }
    public bool IsGranted { get; private set; }
    public string GrantedById { get; private set; } = null!;
    public DateTime? ExpiresAt { get; private set; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    private UserPermissionOverride()
    {
        // EF materialization.
    }

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

    public static UserPermissionOverride Grant(
        string userId, string permission, string grantedById, string? reason = null, DateTime? expiresAt = null) =>
        new(userId, permission, true, grantedById, reason, expiresAt);

    public static UserPermissionOverride Deny(
        string userId, string permission, string grantedById, string? reason = null, DateTime? expiresAt = null) =>
        new(userId, permission, false, grantedById, reason, expiresAt);
}
