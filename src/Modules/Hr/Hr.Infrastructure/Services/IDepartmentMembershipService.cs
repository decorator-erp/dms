using Hr.Domain;
using SharedKernel;

namespace Hr.Infrastructure.Services;

/// <summary>
/// K2: every change to who is in which department, and who heads it, goes through here. Before, seven
/// handlers each wrote <c>employee_departments</c> and <c>DepartmentHeadId</c> their own way, and the
/// rules drifted apart (see the Phase 2 notes). The rules, in one place:
/// <list type="bullet">
/// <item>An employee is active in at most one department (K1). Assigning someone who is already in
/// another department is refused (<c>employees.AlreadyInAnotherDepartment</c>, 409); changing it is
/// <see cref="MoveAsync"/>'s job.</item>
/// <item>A pair keeps one row for life: rejoining a former department restores that row.</item>
/// <item>Only active employees join or head departments (<c>employees.Inactive</c>, 409). A deleted
/// department is gone (404).</item>
/// <item>A head stays a member: removing or moving a head is refused until the head is unassigned
/// (<c>departments.RemoveHeadFirst</c>, 409).</item>
/// <item>Deactivating an employee ends their headships; deleting a department ends its memberships and
/// its headship.</item>
/// </list>
/// Each method saves. Each command already runs in one transaction (<c>DomainEventDispatchDecorator</c>),
/// so a method that saves twice (a move: leave, then join, so the one-active-membership index never sees
/// two) is still all-or-nothing.
/// </summary>
internal interface IDepartmentMembershipService
{
    Task<Result> AssignAsync(Guid employeeId, Guid departmentId, CancellationToken ct);

    Task<Result> MoveAsync(Guid employeeId, Guid newDepartmentId, CancellationToken ct);

    Task<Result> RemoveAsync(Guid employeeId, Guid departmentId, CancellationToken ct);

    Task<Result> AssignHeadAsync(Guid departmentId, Guid employeeId, CancellationToken ct);

    Task<Result> UnassignHeadAsync(Guid departmentId, CancellationToken ct);

    /// <summary>Deactivates <paramref name="employee"/> and clears every headship they hold. Their
    /// membership is kept (it is hidden with them by the employee filter), so reactivation puts them back
    /// where they were.</summary>
    Task<Result> DeactivateEmployeeAsync(Employee employee, CancellationToken ct);

    /// <summary>Soft-deletes the department, ending its memberships and clearing its head.</summary>
    Task<Result> DeactivateDepartmentAsync(Guid departmentId, CancellationToken ct);
}
