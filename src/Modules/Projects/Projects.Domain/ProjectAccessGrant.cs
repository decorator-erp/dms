using System.Linq.Expressions;
using SharedKernel;

namespace Projects.Domain;

/// <summary>
/// P1-A: access to some areas (and optionally some stages) of one project, for an employee who is not on it, for a
/// time. <see cref="EmployeeId"/>, <see cref="GrantedById"/> and <see cref="RevokedById"/> are bare Guids →
/// <c>hr.employees</c>, never navigations, as on <see cref="ProjectMember"/>. Optimistic concurrency on <c>xmin</c>.
///
/// <para><b>Active</b> means not revoked, started, and not yet expired: <see cref="IsActiveAt"/> and the SQL twin
/// <see cref="ActiveAt"/> are the one rule. Nothing is stored about it, so a grant ends on time with no job, and the
/// clock is always the caller's <c>TimeProvider</c>.</para>
///
/// <para><see cref="StageIds"/> is a <c>uuid[]</c> column, not a child table with a foreign key: a cascade from a
/// deleted stage would empty the list, and an empty list means the whole project. A deleted stage's id simply stops
/// matching anything.</para>
///
/// <para>Revoking is final. A revoked or expired grant cannot be edited; a new grant is made instead, so the history
/// stays honest (the entity is <see cref="IAuditable"/>).</para>
/// </summary>
internal sealed class ProjectAccessGrant : Entity, IAuditable
{
    public const int ReasonMaxLength = 500;

    private List<Guid> _stageIds = [];

    public Guid ProjectId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public ProjectMemberAccess Level { get; private set; }
    public ProjectAccessArea Areas { get; private set; }

    /// <summary>Empty: the whole project.</summary>
    public IReadOnlyList<Guid> StageIds => _stageIds;

    public DateTime StartsAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid GrantedById { get; private set; }
    public DateTime GrantedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? RevokedById { get; private set; }
    public string? RevokeReason { get; private set; }

    private ProjectAccessGrant()
    {
        // EF materialization.
    }

    public static ProjectAccessGrant Create(
        Guid projectId, Guid employeeId, ProjectMemberAccess level, ProjectAccessArea areas, IEnumerable<Guid> stageIds,
        DateTime startsAtUtc, DateTime? expiresAtUtc, string reason, Guid grantedById, DateTime nowUtc) =>
        new()
        {
            ProjectId = projectId,
            EmployeeId = employeeId,
            Level = level,
            Areas = areas,
            _stageIds = [.. stageIds.Distinct()],
            StartsAtUtc = startsAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            Reason = reason.Trim(),
            GrantedById = grantedById,
            GrantedAtUtc = nowUtc,
        };

    /// <summary>The one active-window rule: not revoked, <c>StartsAtUtc ≤ now</c>, and no end or <c>now &lt;
    /// ExpiresAtUtc</c>.</summary>
    public bool IsActiveAt(DateTime nowUtc) =>
        RevokedAtUtc is null && StartsAtUtc <= nowUtc && (ExpiresAtUtc is null || nowUtc < ExpiresAtUtc);

    /// <summary><see cref="IsActiveAt"/> as a predicate EF translates to SQL.</summary>
    public static Expression<Func<ProjectAccessGrant, bool>> ActiveAt(DateTime nowUtc) =>
        g => g.RevokedAtUtc == null && g.StartsAtUtc <= nowUtc && (g.ExpiresAtUtc == null || nowUtc < g.ExpiresAtUtc);

    /// <summary>Not revoked and not expired: active now or starting later. At most one per (project, employee).</summary>
    public static Expression<Func<ProjectAccessGrant, bool>> CurrentOrUpcomingAt(DateTime nowUtc) =>
        g => g.RevokedAtUtc == null && (g.ExpiresAtUtc == null || nowUtc < g.ExpiresAtUtc);

    public ProjectAccessGrantStatus StatusAt(DateTime nowUtc) =>
        RevokedAtUtc is not null ? ProjectAccessGrantStatus.Revoked
        : ExpiresAtUtc is { } end && nowUtc >= end ? ProjectAccessGrantStatus.Expired
        : nowUtc < StartsAtUtc ? ProjectAccessGrantStatus.Upcoming
        : ProjectAccessGrantStatus.Active;

    /// <summary>Changes what an Upcoming or Active grant gives. Revoked or Expired: 409 NotEditable.</summary>
    public Result Update(
        ProjectMemberAccess level, ProjectAccessArea areas, IEnumerable<Guid> stageIds, DateTime? expiresAtUtc,
        string reason, DateTime nowUtc)
    {
        if (StatusAt(nowUtc) is ProjectAccessGrantStatus.Revoked or ProjectAccessGrantStatus.Expired)
        {
            return ProjectAccessGrantErrors.NotEditable;
        }

        Level = level;
        Areas = areas;
        _stageIds = [.. stageIds.Distinct()];
        ExpiresAtUtc = expiresAtUtc;
        Reason = reason.Trim();
        return Result.Success();
    }

    /// <summary>Final, at any time before or during the window. A second revoke is 409 AlreadyRevoked; an expired
    /// grant has already ended and keeps that history (409 NotEditable).</summary>
    public Result Revoke(Guid revokedById, string? reason, DateTime nowUtc)
    {
        if (RevokedAtUtc is not null)
        {
            return ProjectAccessGrantErrors.AlreadyRevoked;
        }

        if (StatusAt(nowUtc) == ProjectAccessGrantStatus.Expired)
        {
            return ProjectAccessGrantErrors.NotEditable;
        }

        RevokedAtUtc = nowUtc;
        RevokedById = revokedById;
        RevokeReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return Result.Success();
    }
}
