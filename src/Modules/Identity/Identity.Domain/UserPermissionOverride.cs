using System.Linq.Expressions;
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

    /// <summary>
    /// Aug 2.4: the one definition of a "live" override, as a query predicate: active (Phase 5) and not
    /// expired at <paramref name="utcNow"/>, the same rule as <see cref="IsExpired"/>. Every read that
    /// should ignore dead overrides uses this: the effective-permission resolver, the last-holder check
    /// and the <c>/effective</c> listing. Before, only the resolver filtered, so an expired override still
    /// blocked a new grant (409) and was still listed. Nothing sets <c>IsActive</c> false yet (revoke is a
    /// hard delete); the check is there so a future soft revoke can't leave a row that still counts.
    /// <see cref="IsLive"/> is the same rule for a row already in memory, and a unit test pins the two
    /// to the same answers.
    /// </summary>
    public static Expression<Func<UserPermissionOverride, bool>> LiveAt(DateTime utcNow) =>
        o => o.IsActive && (o.ExpiresAt == null || o.ExpiresAt >= utcNow);

    /// <summary><see cref="LiveAt"/>'s rule over plain values, for rows already loaded (the grant/deny
    /// existence check) and for <see cref="PermissionResolution"/>.</summary>
    public static bool IsLive(bool isActive, DateTime? expiresAt, DateTime utcNow) =>
        isActive && (expiresAt == null || expiresAt >= utcNow);

    public bool IsLiveAt(DateTime utcNow) => IsLive(IsActive, ExpiresAt, utcNow);

    /// <summary>
    /// Aug 2.4: reuses a dead override's row (expired, or inactive since Phase 5) for a new grant or deny.
    /// The (UserId, Permission) index is unique and covers dead rows too, so the new override replaces the
    /// old one in place rather than being inserted next to it. Everything about it is new, including which
    /// way it points: an expired deny can come back as a grant. It is active again.
    /// </summary>
    public void Renew(bool isGranted, string grantedById, string? reason, DateTime? expiresAt)
    {
        SetActive(true);
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
