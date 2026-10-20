using SharedKernel;

namespace Hr.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/EmployeeDepartment.cs</c> — an employee's membership in a
/// department. Composite-keyed on (<see cref="EmployeeId"/>, <see cref="DepartmentId"/>) rather than
/// deriving from <see cref="SharedKernel.Entity"/> the way <see cref="Employee"/>/<see cref="Department"/>
/// do — a join row has no identity of its own beyond the pair it associates, so a surrogate
/// <c>Guid Id</c> would be dead weight, exactly as it was on the backend's own version.
/// <see cref="IsActive"/> here means "removed from the department", not deleted — matches
/// <c>DepartmentDomainService.RemoveEmployeeAsync</c>'s soft-removal in the backend.
///
/// <para>K1: an employee is active in <b>at most one</b> department, which the partial unique index
/// <c>IX_employee_departments_EmployeeId</c> (<c>WHERE "IsActive"</c>) enforces. A pair keeps its one row
/// for life: rejoining a former department <see cref="Restore"/>s that row instead of inserting a second
/// one, which the composite key forbids. <see cref="CreatedAtUtc"/> is when the pair first existed;
/// <see cref="StartedAtUtc"/> is when the current stint began.</para>
/// </summary>
internal sealed class EmployeeDepartment : IAuditable
{
    public Guid EmployeeId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime StartedAtUtc { get; private set; }

    private EmployeeDepartment()
    {
        // EF materialization.
    }

    public static EmployeeDepartment Create(Guid employeeId, Guid departmentId)
    {
        var membership = new EmployeeDepartment { EmployeeId = employeeId, DepartmentId = departmentId };
        membership.StartedAtUtc = membership.CreatedAtUtc;
        return membership;
    }

    public void Remove() => IsActive = false;

    /// <summary>Rejoins the department: active again, with a new stint starting now.</summary>
    public void Restore(DateTime utcNow)
    {
        IsActive = true;
        StartedAtUtc = utcNow;
    }
}
