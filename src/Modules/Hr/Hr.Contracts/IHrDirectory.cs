namespace Hr.Contracts;

/// <summary>
/// HR-owned data only — no join into <c>identity.</c>. <see cref="AppUserId"/> is HR's own bare-string
/// foreign key to Identity's user id (see <c>Hr.Domain.Employee.AppUserId</c>, G2-02), not a navigation.
/// Deliberately carries no name or email: most callers (authorization checks, department membership,
/// assignee resolution) never need one, and forcing a cross-module call onto the hot path just to
/// populate a field nobody reads is exactly the cost this split exists to avoid.
/// </summary>
public sealed record EmployeeSummary(
    Guid Id, string AppUserId, string JobTitle, Guid? PrimaryDepartmentId, bool IsActive);

/// <summary>
/// Composed: one <c>Hr.Contracts</c> query plus one batched <c>Identity.Contracts.IUserDirectory</c>
/// call, zipped in memory (see <see cref="IHrDirectory.GetProfilesAsync"/>). For display paths only —
/// the employee LIST screen, not the hot authorization path. Costs one extra call regardless of how
/// many ids are requested; never looped per id.
/// </summary>
public sealed record EmployeeProfile(
    Guid Id, string AppUserId, string FullName, string? Email,
    string JobTitle, Guid? PrimaryDepartmentId, bool IsActive);

/// <summary>
/// <see cref="HeadEmployeeId"/> mirrors <c>Hr.Domain.Department.DepartmentHeadId</c> — added for
/// Projects' template-instantiation flow (materializing <c>StageTeam</c> rows per department), which
/// must hard-fail if a department has no head. A batch lookup, not a single-id one, for the same reason
/// <see cref="IHrDirectory.GetManyAsync"/> is batched: instantiation resolves every department on a
/// template's stages in one call, never loops per id.
/// </summary>
public sealed record DepartmentHeadSummary(Guid DepartmentId, Guid? HeadEmployeeId);

public interface IHrDirectory
{
    // --- HR-only. No Identity involvement. ---
    Task<Guid?> ResolveEmployeeIdAsync(string appUserId, CancellationToken ct);

    /// <summary>
    /// Whether ANY employee row, active or deactivated, is linked to <paramref name="appUserId"/>.
    /// <see cref="ResolveEmployeeIdAsync"/>, <see cref="GetAsync"/>, <see cref="GetManyAsync"/> and
    /// <see cref="DepartmentMembersAsync"/> see active employees only (K3); this one is for the question
    /// "is this login someone's", which a deactivated employee still answers yes to. The unlinked-login reaper asks it:
    /// a deactivated employee's login is inactive but not orphaned, and hr.employees' RESTRICT foreign key
    /// would refuse its deletion anyway (A1).
    /// </summary>
    Task<bool> IsLinkedAsync(string appUserId, CancellationToken ct);

    /// <summary>The subset of <paramref name="appUserIds"/> that <see cref="IsLinkedAsync"/> answers yes for.</summary>
    Task<IReadOnlyCollection<string>> GetLinkedAsync(IReadOnlyCollection<string> appUserIds, CancellationToken ct);
    Task<EmployeeSummary?> GetAsync(Guid employeeId, CancellationToken ct);
    Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<Guid>> DepartmentMembersAsync(Guid departmentId, CancellationToken ct);
    Task<IReadOnlyList<DepartmentHeadSummary>> GetDepartmentHeadsAsync(IReadOnlyCollection<Guid> departmentIds, CancellationToken ct);

    // --- Composed with IUserDirectory.GetManyAsync. Costs one extra batched call. ---
    Task<IReadOnlyList<EmployeeProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
