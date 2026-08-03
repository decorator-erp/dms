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
/// </summary>
internal sealed class EmployeeDepartment : IAuditable
{
    public Guid EmployeeId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private EmployeeDepartment()
    {
        // EF materialization.
    }

    public static EmployeeDepartment Create(Guid employeeId, Guid departmentId) =>
        new() { EmployeeId = employeeId, DepartmentId = departmentId };

    public void Remove() => IsActive = false;
}
