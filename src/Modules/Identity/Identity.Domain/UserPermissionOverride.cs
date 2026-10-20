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
    /// Aug 2.4: the one definition of a "live" override (not expired at <paramref name="utcNow"/>), the
    /// same rule as <see cref="IsExpired"/>, as a query predicate. Every read that should ignore expired
    /// overrides uses this: the effective-permission resolver, the last-holder check, the grant/deny
    /// existence check and the <c>/effective</c> listing. Before, only the resolver filtered, so an
    /// expired override still blocked a new grant (409) and was still listed.
    /// </summary>
    public static Expression<Func<UserPermissionOverride, bool>> LiveAt(DateTime utcNow) =>
        o => o.ExpiresAt == null || o.ExpiresAt >= utcNow;

    /// <summary>
    /// Aug 2.4: reuses an expired override's row for a new grant or deny. The (UserId, Permission) index
    /// is unique and covers expired rows too, so the new override replaces the old one in place rather
    /// than being inserted next to it. Everything about it is new, including which way it points: an
    /// expired deny can come back as a grant.
    /// </summary>
    public void Renew(bool isGranted, string grantedById, string? reason, DateTime? expiresAt)
    {
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
