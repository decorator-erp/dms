namespace Notifications.Contracts;

/// <summary>
/// Ported verbatim from <c>backend/Domain/Constants/DefaultPermissions.cs</c>' <c>Notifications</c>,
/// <c>Activities</c> and <c>Audit</c> groups. Port, don't tidy — the STRINGS are what a role row
/// stores, and renaming one would silently orphan every grant that already exists.
///
/// <para><b>Flat, not nested by group.</b> The backend groups these under
/// <c>DefaultPermissions.Notifications</c> / <c>.Activities</c> / <c>.Audit</c>, and
/// <c>SitesPermissions.Definitions</c>-style nesting would be the closer port. It is avoided here for
/// one concrete reason: a nested <c>Notifications</c> class inside namespace
/// <c>Notifications.Contracts</c> shadows the namespace itself at every use site. Four constants do not
/// need the grouping enough to pay that.</para>
///
/// <para><b><see cref="AuditRead"/> and <see cref="AuditViewOwn"/> are a scoping split, not a
/// hierarchy.</b> <c>AuditViewOwn</c> reaches exactly one route — <c>/api/audit/my-activity</c>,
/// filtered server-side to <c>ICurrentUser.UserId</c> and never to a caller-supplied parameter. It must
/// not reach the full log or the by-user route <i>at all</i>: a <c>ViewOwn</c> holder allowed into
/// <c>/api/audit/user/{userId}</c> "with a filter applied" is one missing filter away from reading
/// everyone, and that is the phase's only real authorization surface.</para>
/// </summary>
public static class NotificationsPermissions
{
    /// <summary>The caller's own inbox: list, unread count, mark read, mark all read. Self-scoped for
    /// every authenticated user — rows are already caller-scoped in the handler, so this gates the
    /// surface rather than the data.</summary>
    public const string ViewOwn = "notifications.view_own";

    /// <summary>The per-entity activity timeline — the human-readable feed, not the field-level trail.</summary>
    public const string ActivitiesRead = "activities.read";

    /// <summary>The full field-level audit log, entity history, and any user's activity. Admin.</summary>
    public const string AuditRead = "audit.read";

    /// <summary>Self-scoped: <c>/api/audit/my-activity</c> only. See the type-level note.</summary>
    public const string AuditViewOwn = "audit.view_own";

    /// <summary>Every string above, in declaration order. The one list <c>NotificationsModule</c>
    /// registers as its <c>IModulePermissions</c> set — never re-typed at the registration site, and
    /// the value the eight-module permission-count test sums additively rather than hard-coding.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        ViewOwn,
        ActivitiesRead,
        AuditRead,
        AuditViewOwn,
    ];
}
